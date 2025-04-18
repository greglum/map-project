import Foundation
import MapKit

struct AhupuaaFeature: Identifiable {
    let id = UUID()
    let name: String
    let moku: String
    let mokupuni: String
    let acres: Double?  // Make this optional since it might not be available for all features
    let polygon: MKPolygon
    
    // Store the original feature data for reference
    let properties: [String: Any]
}

class AhupuaaDataManager {
    static let shared = AhupuaaDataManager()
    
    private init() {}
    
    func loadAhupuaaData() -> [AhupuaaFeature] {
        guard let url = Bundle.main.url(forResource: "ahupuaa", withExtension: "geojson") else {
            print("Could not find ahupuaa.geojson in bundle")
            return []
        }
        
        do {
            let data = try Data(contentsOf: url)
            return parseGeoJSON(data)
        } catch {
            print("Error loading GeoJSON: \(error)")
            return []
        }
    }
    
    private func parseGeoJSON(_ data: Data) -> [AhupuaaFeature] {
        var features: [AhupuaaFeature] = []
        
        do {
            if let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
               let jsonFeatures = json["features"] as? [[String: Any]] {
                
                for feature in jsonFeatures {
                    if let properties = feature["properties"] as? [String: Any],
                       let geometry = feature["geometry"] as? [String: Any],
                       let type = geometry["type"] as? String,
                       type == "Polygon",
                       let coordinates = geometry["coordinates"] as? [[[Double]]] {
                        
                        // Extract properties
                        let name = properties["ahupuaa"] as? String ?? "Unknown"
                        let moku = properties["moku"] as? String ?? "Unknown"
                        let mokupuni = properties["mokupuni"] as? String ?? "Unknown"
                        let acres = properties["acres"] as? Double
                        
                        // Process coordinates
                        for polygonCoords in coordinates {
                            var coords: [CLLocationCoordinate2D] = []
                            
                            for coord in polygonCoords {
                                if coord.count >= 2 {
                                    let longitude = coord[0]
                                    let latitude = coord[1]
                                    coords.append(CLLocationCoordinate2D(latitude: latitude, longitude: longitude))
                                }
                            }
                            
                            if !coords.isEmpty {
                                let polygon = MKPolygon(coordinates: coords, count: coords.count)
                                let ahupuaa = AhupuaaFeature(
                                    name: name,
                                    moku: moku,
                                    mokupuni: mokupuni,
                                    acres: acres,
                                    polygon: polygon,
                                    properties: properties
                                )
                                features.append(ahupuaa)
                            }
                        }
                    }
                }
            }
        } catch {
            print("Error parsing GeoJSON: \(error)")
        }
        
        return features
    }
}