"""
GeoJSON to DynamoDB Import Script for Ahupuaa GIS Data

This script imports GeoJSON features into a DynamoDB table structured according
to the Ahupuaa schema. It handles proper formatting of geographic data,
generates geohashes, and creates appropriate hierarchical keys.

Usage:
  python import_geojson_to_dynamodb.py

Requirements:
  - boto3, ijson, geohash2
  - AWS credentials configured via AWS CLI or environment variables
  - GeoJSON file with Hawaiian land division data
"""

import boto3
import ijson
import os
import time
import logging
import json
import traceback
import argparse
import sys
import hashlib
import datetime
from decimal import Decimal
from concurrent.futures import ThreadPoolExecutor
from botocore.exceptions import ClientError
import geohash2  # For geohash generation

# Set up logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler("import.log"),
        logging.StreamHandler()
    ]
)
logger = logging.getLogger(__name__)

# Configuration
TABLE_NAME = 'AhupuaaGIS'  # DynamoDB table name from Terraform
# Path to GeoJSON file
GEOJSON_FILE = '/Users/greg/repos/ahupuaa/Ahupuaa.API/Misc/ahupuaa.geojson'
BATCH_SIZE = 25  # DynamoDB BatchWriteItem limit
MAX_WORKERS = 10  # Number of workers for parallel operations
# Simplification factor for map rendering (lower = more simplified)
SIMPLIFIED_COORDS_FACTOR = 0.01
MAX_RETRIES = 10  # Maximum number of retries for write operations
DATA_VERSION = int(datetime.datetime.now().timestamp())

# Initialize DynamoDB resources
try:
    dynamodb = boto3.resource('dynamodb')
    dynamodb_client = boto3.client('dynamodb')
    table = dynamodb.Table(TABLE_NAME)
except Exception as e:
    logger.error(f"Failed to initialize AWS resources: {e}")
    sys.exit(1)


def manage_terraform_infrastructure(action, terraform_dir, vars_file=None):
    """
    Manages the Terraform infrastructure (create, destroy, etc.)

    Args:
        action: The action to perform (init, apply, destroy)
        terraform_dir: Directory containing Terraform files
        vars_file: Optional path to terraform.tfvars file

    Returns:
        bool: True if successful, False otherwise
    """
    import subprocess
    import os

    if not os.path.exists(terraform_dir):
        logger.error(f"Terraform directory not found: {terraform_dir}")
        return False

    # Check if vars file exists
    if vars_file and not os.path.exists(vars_file):
        logger.warning(f"Variables file not found: {vars_file}")
        proceed = input("Continue without variables file? (y/n): ")
        if proceed.lower() != 'y':
            return False
        vars_file = None

    # Change to the Terraform directory
    original_dir = os.getcwd()
    os.chdir(terraform_dir)

    try:
        # Initialize Terraform
        logger.info("Initializing Terraform...")
        init_result = subprocess.run(["terraform", "init"],
                                     check=True,
                                     capture_output=True,
                                     text=True)
        logger.info(init_result.stdout)

        if action == 'apply':
            # Format code
            subprocess.run(["terraform", "fmt"], check=False)

            # Validate configuration
            logger.info("Validating Terraform configuration...")
            validate_result = subprocess.run(["terraform", "validate"],
                                             check=True,
                                             capture_output=True,
                                             text=True)
            logger.info(validate_result.stdout)

            # Create plan
            plan_cmd = ["terraform", "plan", "-out=tfplan"]
            if vars_file:
                plan_cmd.extend(["-var-file", vars_file])

            logger.info("Creating Terraform execution plan...")
            plan_result = subprocess.run(plan_cmd,
                                         check=True,
                                         capture_output=True,
                                         text=True)
            logger.info(plan_result.stdout)

            # Apply changes
            logger.info("Applying Terraform changes...")
            apply_result = subprocess.run(["terraform", "apply", "tfplan"],
                                          check=True,
                                          capture_output=True,
                                          text=True)
            logger.info(apply_result.stdout)

        elif action == 'destroy':
            # Destroy infrastructure
            destroy_cmd = ["terraform", "destroy", "-auto-approve"]
            if vars_file:
                destroy_cmd.extend(["-var-file", vars_file])

            logger.info("Destroying Terraform infrastructure...")
            destroy_result = subprocess.run(destroy_cmd,
                                            check=True,
                                            capture_output=True,
                                            text=True)
            logger.info(destroy_result.stdout)

        logger.info(f"Terraform {action} completed successfully")
        return True

    except subprocess.CalledProcessError as e:
        logger.error(f"Terraform {action} failed: {e}")
        logger.error(f"Output: {e.stdout}")
        logger.error(f"Error: {e.stderr}")
        return False
    finally:
        # Return to original directory
        os.chdir(original_dir)


def clear_table(table_name, confirm=True):
    """
    Clears all items from the specified DynamoDB table.
    This is equivalent to a TRUNCATE operation in SQL.

    Args:
        table_name: Name of the DynamoDB table to clear
        confirm: If True, asks for confirmation before proceeding

    Returns:
        bool: True if successful, False otherwise
    """
    if confirm:
        response = input(
            f"⚠️ WARNING: This will delete ALL items from table '{table_name}'. Are you sure? (y/n): ")
        if response.lower() != 'y':
            logger.info("Table clear operation cancelled.")
            return False

    logger.info(f"Preparing to clear all items from table {table_name}...")

    try:
        # First, get the primary key structure
        table_description = dynamodb_client.describe_table(
            TableName=table_name)
        key_schema = table_description['Table']['KeySchema']

        # Extract the names of partition key and sort key (if exists)
        partition_key = next(key['AttributeName']
                             for key in key_schema if key['KeyType'] == 'HASH')
        sort_key = next((key['AttributeName']
                        for key in key_schema if key['KeyType'] == 'RANGE'), None)

        # Scan and delete items in parallel
        total_deleted = 0
        scan_kwargs = {
            'TableName': table_name,
            'ProjectionExpression': f"#{partition_key}" + (f", #{sort_key}" if sort_key else ""),
            'ExpressionAttributeNames': {f"#{partition_key}": partition_key}
        }

        if sort_key:
            scan_kwargs['ExpressionAttributeNames'][f"#{sort_key}"] = sort_key

        logger.info("Scanning for items to delete...")

        def delete_batch(items):
            """Helper function to delete a batch of items"""
            if not items:
                return 0

            request_items = {
                table_name: [
                    {
                        'DeleteRequest': {
                            'Key': item
                        }
                    } for item in items
                ]
            }

            try:
                dynamodb_client.batch_write_item(RequestItems=request_items)
                return len(items)
            except Exception as e:
                logger.error(f"Failed to delete batch: {e}")
                return 0

        done = False
        start_key = None

        while not done:
            if start_key:
                scan_kwargs['ExclusiveStartKey'] = start_key

            response = dynamodb_client.scan(**scan_kwargs)
            items = []

            for item in response.get('Items', []):
                key = {}
                key[partition_key] = item[partition_key]
                if sort_key and sort_key in item:
                    key[sort_key] = item[sort_key]
                items.append(key)

                # Process items in batches of BATCH_SIZE
                if len(items) >= BATCH_SIZE:
                    deleted = delete_batch(items)
                    total_deleted += deleted
                    logger.info(f"Deleted {total_deleted} items so far...")
                    items = []

            # Delete any remaining items
            if items:
                deleted = delete_batch(items)
                total_deleted += deleted

            # Check if we need to continue scanning
            start_key = response.get('LastEvaluatedKey')
            done = start_key is None

        logger.info(
            f"Successfully cleared {total_deleted} items from table {table_name}")
        return True

    except Exception as e:
        logger.error(f"Failed to clear table: {e}")
        logger.error(traceback.format_exc())
        return False


def replace_floats(obj):
    """
    Recursively converts all float values to Decimal for DynamoDB compatibility.
    Uses precise string conversion to avoid floating-point issues.

    Args:
        obj: The object to process (list, dict, float, or other)

    Returns:
        The object with floats replaced by Decimal
    """
    if isinstance(obj, list):
        return [replace_floats(i) for i in obj]
    elif isinstance(obj, dict):
        return {k: replace_floats(v) for k, v in obj.items()}
    elif isinstance(obj, float):
        # Use string with sufficient precision to maintain accuracy
        # but trim unnecessary trailing zeros
        s = f"{obj:.10f}".rstrip('0').rstrip('.') if obj != 0 else '0'
        return Decimal(s)
    else:
        return obj


def simplify_coordinates(coordinates, factor=SIMPLIFIED_COORDS_FACTOR):
    """
    Simplifies coordinate arrays for more efficient mobile rendering.

    Args:
        coordinates: GeoJSON coordinates array (can be nested)
        factor: Simplification factor (lower = more simplified)

    Returns:
        Simplified coordinates
    """
    if isinstance(coordinates, list):
        if all(isinstance(x, (int, float, Decimal)) for x in coordinates):
            # This is a single coordinate pair, return as is
            return coordinates
        elif len(coordinates) > 100:  # Only simplify if many points
            # This is a list of coordinates or nested lists
            # Keep only a subset of points based on the factor
            # For polygon boundaries, we preserve the shape while reducing points
            step = max(1, int(1/factor))
            # Always keep first and last point for polygons to ensure closure
            if len(coordinates) > 2:
                simplified = [coordinates[0]] + \
                    coordinates[1:-1:step] + [coordinates[-1]]
                return simplified

        # For nested structures, recursively simplify
        return [simplify_coordinates(c, factor) for c in coordinates]

    return coordinates


def write_batch_to_dynamo(batch_items, max_retries=MAX_RETRIES):
    # Use a more efficient parallel processing approach
    from concurrent.futures import ThreadPoolExecutor

    # Split batches into chunks of BATCH_SIZE
    batches = [batch_items[i:i+BATCH_SIZE]
               for i in range(0, len(batch_items), BATCH_SIZE)]

    def process_batch(batch):
        retries = 0
        items_to_process = batch.copy()

        while items_to_process and retries < max_retries:
            try:
                request_items = {TABLE_NAME: items_to_process}
                response = dynamodb_client.batch_write_item(
                    RequestItems=request_items)

                unprocessed = response.get(
                    'UnprocessedItems', {}).get(TABLE_NAME, [])
                if unprocessed:
                    items_to_process = unprocessed
                    retries += 1
                    time.sleep(min(2 ** retries * 0.1, 1.0))
                else:
                    return True
            except Exception as e:
                logger.error(f"Batch error: {e}")
                retries += 1
                time.sleep(min(2 ** retries * 0.5, 5.0))

        return len(items_to_process) == 0

    # Process batches in parallel
    with ThreadPoolExecutor(max_workers=min(10, len(batches))) as executor:
        results = list(executor.map(process_batch, batches))

    return all(results)


def ensure_table_exists(wait_time=60):
    """
    Verifies that the target DynamoDB table exists and is active.

    Args:
        wait_time: Maximum time to wait for table to become active (in seconds)

    Returns:
        bool: True if table exists and is active
    """
    try:
        start_time = time.time()
        while time.time() - start_time < wait_time:
            response = dynamodb_client.describe_table(TableName=TABLE_NAME)
            status = response['Table']['TableStatus']

            if status == 'ACTIVE':
                logger.info(f"Table {TABLE_NAME} exists and is active.")
                return True
            else:
                logger.warning(
                    f"Table {TABLE_NAME} exists but status is {status}. Waiting...")
                time.sleep(5)  # Wait 5 seconds before checking again

        logger.error(
            f"Table {TABLE_NAME} did not become active within {wait_time} seconds.")
        return False

    except dynamodb_client.exceptions.ResourceNotFoundException:
        logger.error(
            f"Table {TABLE_NAME} does not exist, please create it using Terraform first")
        logger.error("Cannot proceed without valid table")
        return False
    except Exception as e:
        logger.error(f"Error checking table existence: {e}")
        return False


def format_hierarchical_key(mokupuni, moku):
    """
    Formats a hierarchical sort key for the DynamoDB schema.

    Args:
        mokupuni: Island name
        moku: District name

    Returns:
        str: Formatted hierarchical key
    """
    # Clean and standardize names
    mokupuni = (mokupuni or "Unknown").strip()
    moku = (moku or "Unknown").strip()

    return f"MOKUPUNI#{mokupuni}#MOKU#{moku}"


def guess_centroid_from_geometry(geometry):
    """
    Estimates a centroid from GeoJSON geometry when not explicitly provided.

    Args:
        geometry: GeoJSON geometry object

    Returns:
        dict: Centroid with lat/lng or None if can't be determined
    """
    try:
        if geometry['type'] == 'Polygon':
            # Average the coordinates of the first (exterior) ring
            coordinates = geometry['coordinates'][0]
            lat_sum = lng_sum = 0
            for coord in coordinates:
                lng_sum += float(coord[0])
                lat_sum += float(coord[1])

            return {
                'lat': Decimal(str(lat_sum / len(coordinates))),
                'lng': Decimal(str(lng_sum / len(coordinates)))
            }
        elif geometry['type'] == 'Point':
            # Point is already a centroid
            return {
                'lat': Decimal(str(geometry['coordinates'][1])),
                'lng': Decimal(str(geometry['coordinates'][0]))
            }
        else:
            return None
    except (KeyError, IndexError, TypeError):
        return None


def extract_bounds_from_geometry(geometry):
    """
    Extracts bounding box from GeoJSON geometry.

    Args:
        geometry: GeoJSON geometry object

    Returns:
        dict: Bounds with northeast and southwest corners or None
    """
    try:
        if geometry['type'] == 'Polygon':
            # Find min/max coordinates
            coordinates = geometry['coordinates'][0]
            lats = [float(c[1]) for c in coordinates]
            lngs = [float(c[0]) for c in coordinates]

            return {
                'northeast': {
                    'lat': Decimal(str(max(lats))),
                    'lng': Decimal(str(max(lngs)))
                },
                'southwest': {
                    'lat': Decimal(str(min(lats))),
                    'lng': Decimal(str(min(lngs)))
                }
            }
        else:
            return None
    except (KeyError, IndexError, TypeError):
        return None


def custom_json_encoder(obj):
    """
    Custom JSON encoder that handles Decimal objects properly.

    Args:
        obj: Object to encode

    Returns:
        Properly serialized value
    """
    if isinstance(obj, Decimal):
        return float(obj)
    raise TypeError(f"Object of type {type(obj)} is not JSON serializable")


def process_geojson(filename, test_mode=False, test_limit=2):
    """
    Processes a GeoJSON file and imports features to DynamoDB.

    Args:
        filename: Path to the GeoJSON file
        test_mode: If True, processes only a limited number of records
        test_limit: Number of records to process in test mode

    Returns:
        bool: True if import was successful
    """
    if not os.path.exists(filename):
        logger.error(f"File not found: {filename}")
        return False

    file_size = os.path.getsize(filename)
    logger.info(
        f"Processing file: {filename} ({file_size / (1024*1024):.2f} MB)")

    if test_mode:
        logger.info(
            f"Running in TEST MODE - will import only {test_limit} records")

    batch = []
    total_processed = 0
    total_features = 0
    start_time = time.time()

    try:
        # First, count total features for progress reporting
        logger.info("Counting total features in file...")
        with open(filename, 'rb') as f:
            for _ in ijson.items(f, 'features.item'):
                total_features += 1

        logger.info(f"Found {total_features} features to process")

        # Adjust total_features for test mode
        if test_mode:
            process_count = min(test_limit, total_features)
            logger.info(
                f"Test mode: Will process {process_count} of {total_features} features")
        else:
            process_count = total_features

        # Second pass to actually process the data
        with open(filename, 'rb') as f:
            # Process features one by one
            for feature_index, feature in enumerate(ijson.items(f, 'features.item')):
                # Stop after reaching test limit in test mode
                if test_mode and total_processed >= test_limit:
                    logger.info(
                        f"Test mode: Reached limit of {test_limit} records, stopping import")
                    break

                # Extract key information
                properties = feature.get('properties', {})

                # Extract ahupuaa, moku, and mokupuni names with fallbacks
                ahupuaa_name = properties.get(
                    'ahupuaa', properties.get('name', f"Ahupuaa_{feature_index}"))
                moku_name = properties.get('moku', 'Unknown')
                mokupuni_name = properties.get('mokupuni', 'Unknown')

                # Structure primary key
                feature_id = feature.get(
                    'id', str(properties.get('objectid', f"{feature_index}")))
                ahupuaa_pk = f"AHUPUAA#{feature_id}"
                hierarchy_sk = format_hierarchical_key(
                    mokupuni_name, moku_name)

                # Process geometry
                geometry = replace_floats(feature.get('geometry', {}))

                # Generate or extract bounds
                bounds = feature.get('bounds')
                if not bounds and geometry:
                    bounds = extract_bounds_from_geometry(geometry)

                # Generate or extract centroid
                centroid = None
                if 'centroid_geopoint' in feature and isinstance(feature['centroid_geopoint'], dict):
                    centroid = feature['centroid_geopoint']
                elif geometry:
                    centroid = guess_centroid_from_geometry(geometry)

                # Generate geohash from centroid
                geohash = feature.get('geohash')
                if not geohash and centroid:
                    try:
                        # Generate geohash from centroid
                        geohash = geohash2.encode(
                            float(centroid['lat']),
                            float(centroid['lng']),
                            precision=7
                        )
                    except Exception as e:
                        logger.warning(
                            f"Could not generate geohash for feature {feature_id}: {e}")
                        geohash = "0000000"  # Default placeholder
                elif not geohash:
                    geohash = "0000000"  # Default when no centroid available

                # First 3 chars for bounding box queries
                geohash_prefix = geohash[:3]

                # Create simplified geometry for mobile rendering
                simplified_geometry = None
                if geometry and 'coordinates' in geometry:
                    simplified_coords = simplify_coordinates(
                        geometry['coordinates'])
                    simplified_geometry = {
                        'type': geometry['type'],
                        'coordinates': simplified_coords
                    }

                # Default zoom level - can be adjusted later based on feature size
                zoom_level = 10

                # Create item for DynamoDB that matches your schema
                item = {
                    'PutRequest': {
                        'Item': {
                            # Primary key - matching your Terraform schema
                            'AhupuaaPK': {'S': ahupuaa_pk},
                            'HierarchySK': {'S': hierarchy_sk},

                            # Attributes used in GSIs
                            'AhupuaaName': {'S': ahupuaa_name},
                            'MokupuniName': {'S': mokupuni_name},
                            'MokuName': {'S': moku_name},
                            'Geohash': {'S': geohash},
                            'GeohashPrefix': {'S': geohash_prefix},
                            'ZoomLevel': {'N': str(zoom_level)},

                            # Store simplified GeoJSON for faster mobile rendering
                            'SimplifiedBoundaries': {'S': json.dumps(
                                simplified_geometry or geometry,
                                default=custom_json_encoder
                            )}
                        }
                    }
                }

                # Add non-key attributes
                if centroid:
                    item['PutRequest']['Item']['Centroid'] = {
                        'M': {
                            'Lat': {'N': str(centroid['lat'])},
                            'Lng': {'N': str(centroid['lng'])}
                        }
                    }

                # Add MapKit annotation point
                if centroid:
                    item['PutRequest']['Item']['AnnotationPoint'] = {
                        'S': json.dumps({
                            'coordinate': [float(centroid['lng']), float(centroid['lat'])],
                            'title': ahupuaa_name,
                            'subtitle': f"{moku_name}, {mokupuni_name}"
                        })
                    }

                # Add display priority based on feature size or importance
                area = properties.get(
                    'gisacres', properties.get('st_areashape', 0))
                try:
                    area_value = float(area)
                    # Large areas get higher priority (will be displayed at lower zoom levels)
                    priority = min(10, max(1, int(area_value / 10000)))
                except (ValueError, TypeError):
                    priority = 5  # Default priority

                item['PutRequest']['Item']['DisplayPriority'] = {
                    'N': str(priority)}

                if bounds:
                    item['PutRequest']['Item']['Bounds'] = {
                        'M': {
                            'Northeast': {
                                'M': {
                                    'Lat': {'N': str(bounds['northeast']['lat'])},
                                    'Lng': {'N': str(bounds['northeast']['lng'])}
                                }
                            },
                            'Southwest': {
                                'M': {
                                    'Lat': {'N': str(bounds['southwest']['lat'])},
                                    'Lng': {'N': str(bounds['southwest']['lng'])}
                                }
                            }
                        }
                    }

                # Add MBR (Minimum Bounding Rectangle) as a separate attribute
                if bounds:
                    item['PutRequest']['Item']['MBR'] = {
                        'S': json.dumps([
                            [float(bounds['southwest']['lng']),
                             float(bounds['southwest']['lat'])],
                            [float(bounds['northeast']['lng']),
                             float(bounds['northeast']['lat'])]
                        ])
                    }

                # Add zoom level range based on feature size
                if bounds:
                    # Calculate approximate size in degrees
                    size_deg = max(
                        float(bounds['northeast']['lat']) -
                        float(bounds['southwest']['lat']),
                        float(bounds['northeast']['lng']) -
                        float(bounds['southwest']['lng'])
                    )

                    # Set min/max zoom based on feature size
                    min_zoom = 5  # Default for large features
                    max_zoom = 16  # Default for detailed view

                    if size_deg < 0.01:  # Very small features
                        min_zoom = 12
                    elif size_deg < 0.05:  # Small features
                        min_zoom = 10
                    elif size_deg < 0.2:  # Medium features
                        min_zoom = 8

                    item['PutRequest']['Item']['MinZoom'] = {
                        'N': str(min_zoom)}
                    item['PutRequest']['Item']['MaxZoom'] = {
                        'N': str(max_zoom)}

                # Add geometry type
                if 'type' in geometry:
                    item['PutRequest']['Item']['GeometryType'] = {
                        'S': geometry['type']}

                # Add full geometry (can be used for detailed analysis)
                item['PutRequest']['Item']['FullGeometry'] = {
                    'S': json.dumps(geometry, default=custom_json_encoder)}

                # Add style properties for the map
                item['PutRequest']['Item']['StyleProperties'] = {
                    'M': {
                        'FillColor': {'S': '#A3C1AD'},   # Default fill color
                        # Default border color
                        'BorderColor': {'S': '#2A6041'},
                        # Default border width
                        'BorderWidth': {'N': '2'}
                    }
                }

                # Create multiple simplification levels
                if geometry and 'coordinates' in geometry:
                    # Simplified (current version - medium detail)
                    medium_coords = simplify_coordinates(
                        geometry['coordinates'], factor=0.01)

                    # High simplification (low detail for low zoom levels)
                    low_coords = simplify_coordinates(
                        geometry['coordinates'], factor=0.005)

                    # Low simplification (high detail for high zoom levels)
                    high_coords = simplify_coordinates(
                        geometry['coordinates'], factor=0.03)

                    item['PutRequest']['Item']['LowDetailBoundaries'] = {
                        'S': json.dumps({
                            'type': geometry['type'],
                            'coordinates': low_coords
                        }, default=custom_json_encoder)
                    }

                    item['PutRequest']['Item']['HighDetailBoundaries'] = {
                        'S': json.dumps({
                            'type': geometry['type'],
                            'coordinates': high_coords
                        }, default=custom_json_encoder)
                    }

                    # Add rendering hints for iOS
                item['PutRequest']['Item']['iOSRenderingHints'] = {
                    'M': {
                        'StrokeWidth': {'N': '2'},
                        'FillOpacity': {'N': '0.5'},
                        'StrokeOpacity': {'N': '0.8'},
                        # Use priority for z-index
                        'ZIndex': {'N': str(priority)},
                        # Solid line by default
                        'LineDashPattern': {'S': '[0]'},
                        # Lighter color when selected
                        'SelectedFillColor': {'S': '#C1E1AD'},
                        # Darker stroke when selected
                        'SelectedStrokeColor': {'S': '#205841'},
                    }
                }

                # Add original properties from GeoJSON
                if properties:
                    properties_map = {}
                    for key, value in replace_floats(properties).items():
                        if isinstance(value, str):
                            properties_map[key] = {'S': value}
                        elif isinstance(value, (int, Decimal)):
                            properties_map[key] = {'N': str(value)}
                        elif isinstance(value, bool):
                            properties_map[key] = {'BOOL': value}
                        elif value is None:
                            properties_map[key] = {'NULL': True}
                        else:
                            # Convert complex types to string
                            properties_map[key] = {'S': json.dumps(
                                value, default=custom_json_encoder)}

                    item['PutRequest']['Item']['Properties'] = {
                        'M': properties_map}

                # Add metadata for client caching
                feature_hash = hashlib.md5(
                    json.dumps(feature, sort_keys=True,
                               default=custom_json_encoder).encode()
                ).hexdigest()

                item['PutRequest']['Item']['Metadata'] = {
                    'M': {
                        'DataVersion': {'N': str(DATA_VERSION)},
                        'FeatureHash': {'S': feature_hash},
                        'LastUpdated': {'S': datetime.datetime.now().isoformat()},
                    }
                }

                # Add to batch
                batch.append(item)

                # Process batch when it reaches DynamoDB limit
                if len(batch) >= BATCH_SIZE:
                    success = write_batch_to_dynamo(batch)
                    if success:
                        total_processed += len(batch)
                        batch = []
                    else:
                        logger.error(
                            f"Failed to write batch at feature {feature_index}")
                        return False

                    # Log progress
                    elapsed = time.time() - start_time
                    progress = (total_processed / total_features) * \
                        100 if total_features > 0 else 0
                    rate = total_processed / elapsed if elapsed > 0 else 0

                    logger.info(
                        f"Progress: {progress:.2f}% ({total_processed}/{total_features}) - Rate: {rate:.2f} items/sec")

        # Process remaining items
        if batch:
            success = write_batch_to_dynamo(batch)
            if success:
                total_processed += len(batch)
            else:
                logger.error("Failed to write final batch")
                return False

        total_time = time.time() - start_time
        logger.info(
            f"Import completed: {total_processed} features imported in {total_time:.2f} seconds")
        logger.info(
            f"Average rate: {total_processed / total_time:.2f} items/sec")

        return True

    except Exception as e:
        logger.error(f"Error processing file: {e}")
        logger.error(traceback.format_exc())
        return False


def update_table_capacity(read_capacity, write_capacity):
    """
    Updates the DynamoDB table's provisioned capacity.

    Args:
        read_capacity: New read capacity units
        write_capacity: New write capacity units

    Returns:
        bool: True if update was successful
    """
    try:
        logger.info(
            f"Updating table capacity to {read_capacity} RCU, {write_capacity} WCU")

        response = dynamodb_client.update_table(
            TableName=TABLE_NAME,
            ProvisionedThroughput={
                'ReadCapacityUnits': read_capacity,
                'WriteCapacityUnits': write_capacity
            }
        )

        # Wait for table to become active
        waiter = dynamodb_client.get_waiter('table_exists')
        waiter.wait(
            TableName=TABLE_NAME,
            WaiterConfig={
                'Delay': 5,
                'MaxAttempts': 20
            }
        )

        logger.info("Table capacity updated successfully")
        return True

    except Exception as e:
        logger.error(f"Failed to update table capacity: {e}")
        return False


def parse_arguments():
    """
    Parse command line arguments for the script.

    Returns:
        argparse.Namespace: Parsed command-line arguments
    """
    parser = argparse.ArgumentParser(
        description='Import GeoJSON features to DynamoDB')
    parser.add_argument('--test', action='store_true',
                        help='Run in test mode (imports only 2 records)')
    parser.add_argument('--limit', type=int, default=2,
                        help='Number of records to import in test mode (default: 2)')
    parser.add_argument('--recreate-table', action='store_true',
                        help='Recreate the DynamoDB table before importing')
    parser.add_argument('--terraform-dir', type=str,
                        default='/Users/greg/repos/ahupuaa/Ahupuaa.API/Terraform/aws_dynamodb',
                        help='Directory containing Terraform files')
    parser.add_argument('--env', type=str,
                        default='dev',
                        choices=['dev', 'staging', 'prod'],
                        help='Environment to deploy (dev, staging, prod)')
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_arguments()

    # Display welcome message
    print("=" * 80)
    print(f"Ahupuaa GIS Data Import Tool")
    print(f"Target Table: {TABLE_NAME}")
    print(f"Source File: {GEOJSON_FILE}")

    if args.test:
        print(f"Mode: TEST (importing {args.limit} records only)")
    else:
        print(f"Mode: PRODUCTION (importing all records)")
    print("=" * 80)

    # Set vars file based on environment
    vars_file = f"{args.env}.tfvars"
    vars_file_path = os.path.join(args.terraform_dir, vars_file)

    # Check if the vars file exists
    if not os.path.exists(vars_file_path):
        logger.warning(
            f"Variables file for {args.env} environment not found: {vars_file_path}")
        create_vars = input(
            f"Would you like to create a {vars_file} file now? (y/n): ")
        if create_vars.lower() == 'y':
            table_name = input(
                f"Enter table name for {args.env} environment [AhupuaaGIS_{args.env}]: ") or f"AhupuaaGIS_{args.env}"
            read_capacity = input("Enter read capacity units [5]: ") or "5"
            write_capacity = input("Enter write capacity units [5]: ") or "5"

            # Create the vars file
            with open(vars_file_path, 'w') as f:
                f.write(f'table_name = "{table_name}"\n')
                f.write(f'read_capacity = {read_capacity}\n')
                f.write(f'write_capacity = {write_capacity}\n')

            print(f"✅ Created {vars_file_path}")
        else:
            logger.error(
                f"Cannot proceed without variables file for {args.env} environment")
            sys.exit(1)

    # Update TABLE_NAME from vars file
    try:
        with open(vars_file_path, 'r') as f:
            for line in f:
                if line.strip().startswith('table_name'):
                    # Extract table name from tfvars file
                    table_name_match = line.split(
                        '=')[1].strip().replace('"', '').replace("'", "")
                    if table_name_match:
                        TABLE_NAME = table_name_match
                        print(
                            f"Using table name from {vars_file}: {TABLE_NAME}")
                    break
    except Exception as e:
        logger.warning(
            f"Could not extract table name from {vars_file_path}: {e}")

    # Recreate table using Terraform if requested
    if args.recreate_table:
        print(
            f"\nRecreating DynamoDB table for {args.env} environment using Terraform...")

        # First destroy existing infrastructure
        destroy_success = manage_terraform_infrastructure(
            'destroy',
            args.terraform_dir,
            vars_file_path
        )

        if not destroy_success:
            print("⚠️ Warning: Failed to destroy existing infrastructure.")
            proceed = input(
                "Do you want to continue with the apply step anyway? (y/n): ")
            if proceed.lower() != 'y':
                sys.exit(1)

        # Create new infrastructure
        apply_success = manage_terraform_infrastructure(
            'apply',
            args.terraform_dir,
            vars_file_path  # Use this variable which was correctly defined above
        )

        if not apply_success:
            logger.error("Failed to create DynamoDB table. Exiting.")
            sys.exit(1)

            print(
                "✅ Terraform apply successful! Waiting for DynamoDB table to be ready...")

        # Initialize new DynamoDB resources after table creation
        try:
            # Reinitialize the AWS resources after table creation
            dynamodb = boto3.resource('dynamodb')
            dynamodb_client = boto3.client('dynamodb')
            table = dynamodb.Table(TABLE_NAME)

            # Wait up to 60 seconds for the table to become active
            if ensure_table_exists(wait_time=60):
                print("✅ DynamoDB table is now active and ready for use!")
            else:
                logger.error(
                    "DynamoDB table was not ready in the expected time. Exiting.")
                sys.exit(1)
        except Exception as e:
            logger.error(
                f"Failed to initialize AWS resources after table creation: {e}")
            sys.exit(1)

    # Ensure the table exists
    if not ensure_table_exists():
        logger.error("Table validation failed. Exiting.")
        sys.exit(1)

    # Continue with the rest of your existing code...
    # Optionally update capacity before import for faster processing
    try:
        if not args.test:  # Skip for test mode
            increase_capacity = input(
                "Would you like to temporarily increase table capacity for faster import? (y/n): ")
            if increase_capacity.lower() == 'y':
                temp_write_capacity = 25  # Higher capacity for import
                update_table_capacity(5, temp_write_capacity)
    except Exception as e:
        logger.warning(f"Failed to update capacity: {e}")

    # Clear the table before importing new data
    if args.test:
        clear_confirm = input(
            "Test mode: Do you want to clear the table before importing test data? (y/n): ")
        if clear_confirm.lower() == 'y':
            clear_success = clear_table(TABLE_NAME)
            if not clear_success:
                logger.error("Failed to clear table. Exiting.")
                sys.exit(1)
    else:
        clear_success = clear_table(TABLE_NAME)
        if not clear_success:
            logger.error("Failed to clear table. Exiting.")
            sys.exit(1)

    # Run the import
    print("\nStarting import process...")
    success = process_geojson(
        GEOJSON_FILE, test_mode=args.test, test_limit=args.limit)

    # Reset capacity after import if it was increased
    if success and 'temp_write_capacity' in locals():
        try:
            logger.info("Restoring original table capacity")
            update_table_capacity(5, 5)  # Back to default
        except Exception as e:
            logger.warning(f"Failed to restore capacity: {e}")

    if success:
        print("\n✅ Import completed successfully!")
        if args.test:
            print(f"Imported {args.limit} test records.")
        sys.exit(0)
    else:
        print("\n❌ Import failed. Check the logs for details.")
        sys.exit(1)
