variable "aws_region" {
  description = "AWS region for resources"
  type        = string
  default     = "us-west-2"
}

variable "environment" {
  description = "Deployment environment"
  type        = string
  default     = "Development"
}

variable "table_name" {
  description = "Name of the DynamoDB table"
  type        = string
  default     = "AhupuaaGIS"
}

variable "read_capacity" {
  description = "Read capacity units for the table and GSIs"
  type        = number
  default     = 5
}

variable "write_capacity" {
  description = "Write capacity units for the table and GSIs"
  type        = number
  default     = 5
}

variable "enable_pitr" {
  description = "Enable point-in-time recovery"
  type        = bool
  default     = false
}

variable "enable_encryption" {
  description = "Enable server-side encryption"
  type        = bool
  default     = true
}

variable "project_name" {
  description = "Project name for resource tagging"
  type        = string
  default     = "Hawaii GIS Data"
}

variable "gsi_projections" {
  description = "Configuration for GSI projections"
  type = map(object({
    projection_type    = string
    non_key_attributes = list(string)
  }))
  default = {
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
}