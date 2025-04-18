//
//  ViewController.swift
//  ahupuaa-app
//
//  Created by Greg  on 4/16/25.
//

import UIKit
import SwiftUI
import MapKit

class ViewController: UIViewController {
    
    override func viewDidLoad() {
        super.viewDidLoad()
        
        // Set up the SwiftUI view to display the map
        setupMapView()
    }
    
    private func setupMapView() {
        // Create the SwiftUI view - use AhupuaaMapView instead of MapView
        let mapView = AhupuaaMapView()
        
        // Create a hosting controller to wrap the SwiftUI view
        let hostingController = UIHostingController(rootView: mapView)
        
        // Add the hosting controller as a child view controller
        addChild(hostingController)
        
        // Add the hosting controller's view as a subview
        view.addSubview(hostingController.view)
        
        // Set up constraints to fill the parent view
        hostingController.view.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            hostingController.view.topAnchor.constraint(equalTo: view.topAnchor),
            hostingController.view.leftAnchor.constraint(equalTo: view.leftAnchor),
            hostingController.view.rightAnchor.constraint(equalTo: view.rightAnchor),
            hostingController.view.bottomAnchor.constraint(equalTo: view.bottomAnchor)
        ])
        
        // Notify the hosting controller that it's now embedded in the parent
        hostingController.didMove(toParent: self)
    }
}

