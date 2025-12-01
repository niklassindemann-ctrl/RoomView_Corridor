# Birds-Eye View Setup Guide

## Overview

The birds-eye/tabletop view mode allows users to view and interact with the VR environments (corridor, staircase) from above, as if looking down at an architectural model on a table. This view mode is automatically activated based on the `TaskVariant` setting in `ExperimentDataManager`.

## How It Works

The system uses **startup-based view selection** - no runtime switching needed:

1. **Set TaskVariant** in `ExperimentDataManager` inspector:
   - `RoomView_Corridor` → User spawns inside corridor at eye level
   - `RoomView_Staircase` → User spawns inside staircase at eye level
   - `BirdsEye_Corridor` → User spawns above corridor, looking down at 30°
   - `BirdsEye_Staircase` → User spawns above staircase, looking down at 30°

2. **Build separate APKs** for each condition (matches your experimental design)

3. **ViewModeManager** automatically detects the TaskVariant and configures:
   - XR Origin position and rotation
   - Appropriate locomotion system
   - View constraints

## Setup Instructions

### 1. Add ViewModeManager to Scene

1. In Hierarchy, find or create the **ExperimentManager** GameObject
2. Add component: **`ViewModeManager`** (namespace: `Player`)
3. Configure in Inspector:

#### Required References:
- **XR Origin**: Drag "XR Origin (XR Rig)" from Hierarchy
- **Ground Plane**: Drag the "Plane" GameObject (the table/ground)
- **Environment Center**: 
  - For Corridor: Drag "18.11.2025 Corridor" GameObject
  - For Staircase: Drag "27.11.2025 staircase" or "28.11.2025 L shape" GameObject
  - (Or leave empty - system will auto-find)

#### Room View Anchors:
- **Room View Anchor Corridor**: Create empty GameObject at desired spawn point inside corridor
  - Position: e.g., `(7.04, 1, 10)` (your current PlayerSpawn position)
  - Rotation: Face forward direction
- **Room View Anchor Staircase**: Create empty GameObject at desired spawn point inside staircase

#### Birds-Eye Settings:
- **Birds Eye Height**: `3.0` (meters above ground plane)
- **Birds Eye Distance**: `4.0` (meters horizontal offset from environment center)
- **Birds Eye Pitch Degrees**: `30` (downward viewing angle)

#### Locomotion Components:
- **Room View Locomotion**: Drag your existing `VRPlayerController` or locomotion component
- **Birds Eye Locomotion**: Leave empty - will be auto-created on XR Origin

### 2. Configure ExperimentDataManager

1. Select **ExperimentManager** GameObject
2. In `ExperimentDataManager` component:
   - Set **Task Variant** to desired condition:
     - `BirdsEye_Corridor` for birds-eye corridor view
     - `BirdsEye_Staircase` for birds-eye staircase view
     - `RoomView_Corridor` for room view corridor
     - `RoomView_Staircase` for room view staircase

### 3. Create Anchor Transforms (Optional but Recommended)

#### Room View Anchors:
1. Create empty GameObject: `Right-click → Create Empty`
2. Name it: **`RoomViewAnchor_Corridor`**
3. Position it inside the corridor at eye level (e.g., `(7.04, 1, 10)`)
4. Rotate it to face the desired starting direction
5. Drag to **ViewModeManager → Room View Anchor Corridor**

6. Repeat for staircase: **`RoomViewAnchor_Staircase`**

#### Birds-Eye Anchors (Auto-Calculated):
- No manual anchors needed - system calculates position based on:
  - Environment center position
  - Ground plane Y position
  - Height and distance settings

### 4. Verify Locomotion Components

The system automatically:
- **Disables** room view locomotion (VRPlayerController) in birds-eye mode
- **Enables** birds-eye locomotion (BirdsEyeLocomotion) in birds-eye mode
- **BirdsEyeLocomotion** component is auto-added to XR Origin if missing

## Birds-Eye Locomotion Behavior

### Movement:
- **Left thumbstick**: Move around the model (XZ plane relative to head direction)
- Movement is constrained to stay:
  - Above the ground plane (between `minHeight` and `maxHeight`)
  - At appropriate distance from environment center (between `minRadius` and `maxRadius`)
  - Never drops into rooms or goes too far away

### Constraints (Configurable in ViewModeManager):
- **Min Height**: `1.5m` (prevents dropping into rooms)
- **Max Height**: `6.0m` (prevents going too high)
- **Min Radius**: `2.0m` (prevents getting too close to model center)
- **Max Radius**: `8.0m` (prevents going too far away)

### Head Tracking:
- Normal VR head tracking works - user can tilt head, look around
- Camera pitch is NOT locked - user can look up/down naturally
- The "isometric feeling" comes from the starting pose (30° down) and movement constraints

## Testing

### In Editor:
1. Set **Task Variant** to `BirdsEye_Corridor` in ExperimentManager
2. Press Play
3. You should spawn above the corridor, looking down at ~30°
4. Use left thumbstick to move around (if you have VR controllers connected)

### On Quest:
1. Build APK with Task Variant set to `BirdsEye_Corridor`
2. Install and run
3. User spawns in birds-eye view automatically
4. Waypoint placement and path planning work normally - only vantage point changes

## Troubleshooting

### "ViewModeManager: ExperimentDataManager not found"
- Ensure `ExperimentManager` GameObject exists and has `ExperimentDataManager` component
- Ensure it's enabled and not destroyed

### "Cannot setup BirdsEyeView: Missing XR Origin or environment center"
- Assign XR Origin in ViewModeManager inspector
- Assign Environment Center (or ensure environment GameObject exists with expected name)

### User spawns in wrong position
- Check that Ground Plane is assigned correctly
- Verify Environment Center points to the correct environment GameObject
- Adjust Birds Eye Height/Distance settings in ViewModeManager

### Locomotion not working in birds-eye view
- Check that BirdsEyeLocomotion component exists on XR Origin
- Verify it's enabled (should auto-enable when ViewModeManager activates birds-eye mode)
- Check that left controller is connected and thumbstick input is working

### User can drop into rooms
- Increase `Min Height` in ViewModeManager (e.g., to `2.0m` or higher)
- Check that Ground Plane Y position is correct

## Integration with Existing Systems

### Waypoint Placement:
- Works normally in both views
- `PointPlacementManager` and `RayDepthController` are view-agnostic
- Ghost preview and collision detection work the same

### Path Planning:
- `FlightPathManager` and `PathRenderer` work in both views
- Path lines and badges render correctly from any vantage point

### Experiment Tracking:
- `ExperimentDataManager` automatically records view mode in metadata
- View mode is determined from TaskVariant and saved to JSON

## Design Decisions

### Why Startup-Based (Not Runtime Switching)?
1. **Matches experimental design**: Each build = one condition
2. **Simpler implementation**: No mode switching logic needed
3. **Cleaner data**: Each trial has a single, consistent view mode
4. **Less complexity**: No UI needed for mode switching

### Why Not Lock Camera Pitch?
- Natural head tracking is important for VR comfort
- User can still look around naturally
- The starting pose (30° down) and constraints provide the "tabletop" feeling
- Locking pitch would feel unnatural and cause motion sickness

### Why Constrain Movement?
- Prevents user from accidentally dropping into rooms
- Keeps user at appropriate viewing distance
- Maintains the "bird above model" metaphor
- Still allows natural movement around the model

## Future Enhancements (Optional)

If you later want runtime switching:
1. Add `SetViewMode(ViewMode mode)` public method to ViewModeManager
2. Add UI button or controller input to call it
3. Add smooth transition animation between views
4. Update ExperimentDataManager to track view mode changes

But for now, the startup-based approach is simpler and matches your experimental needs.

