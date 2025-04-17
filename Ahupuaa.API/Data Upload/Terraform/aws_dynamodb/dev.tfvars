aws_region        = "us-west-2"
environment       = "Development"
table_name        = "AhupuaaGIS"
read_capacity     = 5
write_capacity    = 5
enable_pitr       = false
enable_encryption = true
project_name      = "Hawaii GIS Data"

# GSI projections need to be in proper HCL format
gsi_projections = {
  AhupuaaIndex = {
    projection_type    = "ALL"
    non_key_attributes = []
  }
  MokuIndex = {
    projection_type    = "ALL"
    non_key_attributes = []
  }
  GeospatialIndex = {
    projection_type    = "ALL"
    non_key_attributes = []
  }
  MokupuniIndex = {
    projection_type    = "ALL"
    non_key_attributes = []
  }
  ZoomLevelIndex = {
    projection_type    = "INCLUDE"
    non_key_attributes = ["SimplifiedBoundaries", "AhupuaaName", "MokupuniName"]
  }
  GeoBoundingBoxIndex = {
    projection_type    = "INCLUDE"
    non_key_attributes = ["SimplifiedBoundaries", "AhupuaaName", "MokupuniName", "MokuName"]
  }
}