// ContentView.swift
import SwiftUI

struct ContentView: View {
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
        // Load the data using the shared manager
        let features = AhupuaaDataManager.shared.loadAhupuaaData()
        
        // Update the state on the main thread
        DispatchQueue.main.async {
            self.ahupuaaFeatures = features
            self.isDataLoaded = true
        }
    }
}

#Preview {
    ContentView()
}