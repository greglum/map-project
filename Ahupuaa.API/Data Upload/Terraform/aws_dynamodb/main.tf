terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 4.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

resource "aws_dynamodb_table" "ahupuaa_gis" {
  name           = var.table_name
  billing_mode   = "PROVISIONED"
  read_capacity  = var.read_capacity
  write_capacity = var.write_capacity
  hash_key       = "AhupuaaPK"
  range_key      = "HierarchySK"

  attribute {
    name = "AhupuaaPK"
    type = "S"
  }

  attribute {
    name = "HierarchySK"
    type = "S"
  }

  attribute {
    name = "AhupuaaName"
    type = "S"
  }

  attribute {
    name = "MokupuniName"
    type = "S"
  }

  attribute {
    name = "MokuName"
    type = "S"
  }

  attribute {
    name = "Geohash"
    type = "S"
  }

  attribute {
    name = "ZoomLevel"
    type = "N"
  }

  attribute {
    name = "GeohashPrefix"
    type = "S"
  }

  global_secondary_index {
    name            = "AhupuaaIndex"
    hash_key        = "AhupuaaName"
    projection_type = var.gsi_projections["AhupuaaIndex"].projection_type
    read_capacity   = var.read_capacity
    write_capacity  = var.write_capacity
  }

  global_secondary_index {
    name            = "MokuIndex"
    hash_key        = "MokuName"
    range_key       = "HierarchySK"
    projection_type = var.gsi_projections["MokuIndex"].projection_type
    read_capacity   = var.read_capacity
    write_capacity  = var.write_capacity
  }

  global_secondary_index {
    name            = "GeospatialIndex"
    hash_key        = "Geohash"
    range_key       = "AhupuaaPK"
    projection_type = var.gsi_projections["GeospatialIndex"].projection_type
    read_capacity   = var.read_capacity
    write_capacity  = var.write_capacity
  }

  global_secondary_index {
    name            = "MokupuniIndex"
    hash_key        = "MokupuniName"
    range_key       = "HierarchySK"
    projection_type = var.gsi_projections["MokupuniIndex"].projection_type
    read_capacity   = var.read_capacity
    write_capacity  = var.write_capacity
  }

  global_secondary_index {
    name               = "ZoomLevelIndex"
    hash_key           = "ZoomLevel"
    range_key          = "Geohash"
    projection_type    = var.gsi_projections["ZoomLevelIndex"].projection_type
    non_key_attributes = var.gsi_projections["ZoomLevelIndex"].non_key_attributes
    read_capacity      = var.read_capacity
    write_capacity     = var.write_capacity
  }

  global_secondary_index {
    name               = "GeoBoundingBoxIndex"
    hash_key           = "GeohashPrefix"
    range_key          = "AhupuaaPK"
    projection_type    = var.gsi_projections["GeoBoundingBoxIndex"].projection_type
    non_key_attributes = var.gsi_projections["GeoBoundingBoxIndex"].non_key_attributes
    read_capacity      = var.read_capacity
    write_capacity     = var.write_capacity
  }

  tags = {
    Name        = var.table_name
    Environment = var.environment
    Project     = var.project_name
  }

  point_in_time_recovery {
    enabled = var.enable_pitr
  }

  server_side_encryption {
    enabled = var.enable_encryption
  }
}

output "ahupuaa_gis_arn" {
  value       = aws_dynamodb_table.ahupuaa_gis.arn
  description = "The ARN for the DynamoDB table"
}
