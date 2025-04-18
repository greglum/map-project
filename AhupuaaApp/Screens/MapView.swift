// MapView.swift
import SwiftUI
import MapKit

struct MapView: UIViewRepresentable {
    @Binding var selectedAhupuaa: AhupuaaFeature?
    var ahupuaaFeatures: [AhupuaaFeature]
    
    func makeUIView(context: Context) -> MKMapView {
        let mapView = MKMapView()
        mapView.delegate = context.coordinator
        
        // Set initial region
        mapView.setRegion(MKCoordinateRegion(
            center: CLLocationCoordinate2D(latitude: 20.2734, longitude: -156.3319),
            span: MKCoordinateSpan(latitudeDelta: 6.0, longitudeDelta: 6.0)
        ), animated: false)
        
        // Add tap gesture recognizer
        let tapGesture = UITapGestureRecognizer(target: context.coordinator, action: #selector(Coordinator.handleTap(_:)))
        tapGesture.cancelsTouchesInView = false  // Allow map interactions to still work
        mapView.addGestureRecognizer(tapGesture)
        
        return mapView
    }
    
    func updateUIView(_ mapView: MKMapView, context: Context) {
        // Always ensure all features are on the map
        if mapView.overlays.count != ahupuaaFeatures.count {
            mapView.removeOverlays(mapView.overlays)
            for feature in ahupuaaFeatures {
                mapView.addOverlay(feature.polygon)
            }
        }
        
        // Check if selection changed
        if context.coordinator.selectedPolygon !== selectedAhupuaa?.polygon {
            context.coordinator.selectedPolygon = selectedAhupuaa?.polygon
            
            // Remove and re-add overlays to refresh appearance
            mapView.removeOverlays(mapView.overlays)
            for feature in ahupuaaFeatures {
                mapView.addOverlay(feature.polygon)
            }
        }
    }
    
    func makeCoordinator() -> Coordinator {
        Coordinator(self)
    }
    
    class Coordinator: NSObject, MKMapViewDelegate {
        var parent: MapView
        var selectedPolygon: MKPolygon?
        
        init(_ parent: MapView) {
            self.parent = parent
            self.selectedPolygon = nil
        }
        
        func mapView(_ mapView: MKMapView, rendererFor overlay: MKOverlay) -> MKOverlayRenderer {
            if let polygon = overlay as? MKPolygon {
                let renderer = MKPolygonRenderer(polygon: polygon)
                
                // Check if this is the selected feature
                if let selectedPolygon = parent.selectedAhupuaa?.polygon,
                   polygon === selectedPolygon {
                    renderer.fillColor = UIColor.systemRed.withAlphaComponent(0.3)
                    renderer.strokeColor = UIColor.systemRed
                    renderer.lineWidth = 2
                } else {
                    renderer.fillColor = UIColor.systemBlue.withAlphaComponent(0.2)
                    renderer.strokeColor = UIColor.systemBlue
                    renderer.lineWidth = 1
                }
                
                return renderer
            }
            return MKOverlayRenderer(overlay: overlay)
        }
        
        @objc func handleTap(_ gesture: UITapGestureRecognizer) {
            guard let mapView = gesture.view as? MKMapView else { return }
            
            let point = gesture.location(in: mapView)
            let coordinate = mapView.convert(point, toCoordinateFrom: mapView)
            
            // Check if the tap is inside any polygon
            for feature in parent.ahupuaaFeatures {
                let polygon = feature.polygon
                let renderer = MKPolygonRenderer(polygon: polygon)
                let mapPoint = MKMapPoint(coordinate)
                let rendererPoint = renderer.point(for: mapPoint)
                
                if renderer.path.contains(rendererPoint) {
                    DispatchQueue.main.async {
                        self.parent.selectedAhupuaa = feature
                    }
                    return
                }
            }
            
            // If tap is outside any polygon, deselect
            DispatchQueue.main.async {
                self.parent.selectedAhupuaa = nil
            }
        }
    }
}