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
        print("MapView.updateUIView called with \(ahupuaaFeatures.count) features, selected: \(selectedAhupuaa?.name ?? "none")")
        
        // Always ensure all features are on the map
        if mapView.overlays.count != ahupuaaFeatures.count {
            print("Features count changed, updating all overlays")
            mapView.removeOverlays(mapView.overlays)
            for feature in ahupuaaFeatures {
                mapView.addOverlay(feature.polygon)
            }
        }
        
        // Check if selection changed
        if context.coordinator.selectedPolygon !== selectedAhupuaa?.polygon {
            print("Selection changed, redrawing overlays")
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
            guard let mapView = gesture.view as? MKMapView else { 
                print("ERROR: MapView not found")
                return 
            }
            
            let point = gesture.location(in: mapView)
            let coordinate = mapView.convert(point, toCoordinateFrom: mapView)
            print("Tap detected at: \(coordinate.latitude), \(coordinate.longitude)")
            
            // Debug: Check if we have features to test against
            print("Number of features to test: \(parent.ahupuaaFeatures.count)")
            print("Number of overlays on map: \(mapView.overlays.count)")
            
            // Check if the tap is inside any polygon
            for (index, feature) in parent.ahupuaaFeatures.enumerated() {
                let polygon = feature.polygon
                let renderer = MKPolygonRenderer(polygon: polygon)
                let mapPoint = MKMapPoint(coordinate)
                let rendererPoint = renderer.point(for: mapPoint)
                
                print("Testing polygon \(index): \(feature.name)")
                
                if renderer.path.contains(rendererPoint) {
                    print("HIT! Selected ahupuaʻa: \(feature.name)")
                    DispatchQueue.main.async {
                        self.parent.selectedAhupuaa = feature
                    }
                    return
                }
            }
            
            // If tap is outside any polygon, deselect
            print("No polygon found at tap location")
            DispatchQueue.main.async {
                self.parent.selectedAhupuaa = nil
            }
        }
    }
}

// Main container view
struct AhupuaaMapView: View {
    @State private var ahupuaaFeatures: [AhupuaaFeature] = []
    @State private var selectedAhupuaa: AhupuaaFeature?
    @State private var isDataLoaded = false
    
    var body: some View {
        ZStack {
            MapView(selectedAhupuaa: $selectedAhupuaa, ahupuaaFeatures: ahupuaaFeatures)
                .edgesIgnoringSafeArea(.all)
                .id(isDataLoaded) // Force view refresh when data loads
            
            if let selected = selectedAhupuaa {
                VStack {
                    Spacer()
                    
                    VStack(alignment: .leading, spacing: 8) {
                        Text(selected.name)
                            .font(.title)
                            .fontWeight(.bold)
                        
                        Text("Ahupuaʻa: \(selected.name)")
                        Text("Moku: \(selected.moku)")
                        Text("Mokupuni: \(selected.mokupuni)")
                        if let acres = selected.acres {
                            Text("Acres: \(String(format: "%.2f", acres))")
                        }
                    }
                    .padding()
                    .background(Color.white.opacity(0.9))
                    .cornerRadius(12)
                    .padding()
                    .shadow(radius: 5)
                }
            }
        }
        .onAppear {
            loadAhupuaaData()
        }
    }
    
    private func loadAhupuaaData() {
        print("Starting to load ahupuaʻa data...")
        
        // Load the data using the shared manager
        let features = AhupuaaDataManager.shared.loadAhupuaaData()
        print("Loaded \(features.count) features")
        
        // Important: Update the state on the main thread
        DispatchQueue.main.async {
            self.ahupuaaFeatures = features
            self.isDataLoaded = true
            print("Updated ahupuaaFeatures array with \(self.ahupuaaFeatures.count) features")
        }
    }
}

struct MapView_Previews: PreviewProvider {
    static var previews: some View {
        // Create a preview that forces data loading
        AhupuaaMapView()
            .onAppear {
                // Force data loading for preview
                _ = AhupuaaDataManager.shared.loadAhupuaaData()
            }
    }
}