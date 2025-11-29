# Birds-Eye View Implementation Summary

## Design Decision: Startup-Based View Selection

**Why not runtime switching?**
- Your experimental design uses separate builds per condition (TaskVariant)
- Simpler implementation - no mode switching UI needed
- Cleaner data - each trial has one consistent view mode
- Matches your existing ExperimentDataManager structure

**How it works:**
1. Set `TaskVariant` in `ExperimentDataManager` inspector
2. `ViewModeManager` reads it at startup
3. Automatically configures view mode (RoomView or BirdsEyeView)
4. Build separate APKs for each condition

## What Was Implemented

### 1. ViewModeManager.cs
- **Location**: `Assets/Scripts/Player/ViewModeManager.cs`
- **Purpose**: Central manager that configures view mode at startup
- **Key Features**:
  - Reads TaskVariant from ExperimentDataManager
  - Determines view mode (RoomView vs BirdsEyeView)
  - Positions XR Origin appropriately
  - Enables/disables locomotion components
  - Auto-finds environment references if not assigned

### 2. BirdsEyeLocomotion.cs
- **Location**: `Assets/Scripts/Player/BirdsEyeLocomotion.cs`
- **Purpose**: Movement system for birds-eye/tabletop view
- **Key Features**:
  - Left thumbstick movement (XZ plane relative to head)
  - Height constraints (min/max above ground)
  - Radius constraints (min/max distance from environment center)
  - Prevents dropping into rooms or going too far away

### 3. Documentation
- **BIRDSEYE_VIEW_SETUP.md**: Complete setup guide
- **IMPLEMENTATION_SUMMARY.md**: This file

## Unity Setup Checklist

### Step 1: Add ViewModeManager
- [ ] Select `ExperimentManager` GameObject in Hierarchy
- [ ] Add Component → `ViewModeManager` (namespace: `Player`)
- [ ] Assign **XR Origin**: Drag "XR Origin (XR Rig)"
- [ ] Assign **Ground Plane**: Drag "Plane" GameObject
- [ ] Assign **Environment Center**: 
  - For Corridor: Drag "18.11.2025 Corridor"
  - For Staircase: Drag "27.11.2025 staircase" or "28.11.2025 L shape"
  - (Or leave empty - will auto-find)

### Step 2: Create Room View Anchors (Optional)
- [ ] Create empty GameObject: `RoomViewAnchor_Corridor`
  - Position: Inside corridor at eye level (e.g., `(7.04, 1, 10)`)
  - Rotation: Face desired starting direction
  - Assign to ViewModeManager → Room View Anchor Corridor
- [ ] Create empty GameObject: `RoomViewAnchor_Staircase`
  - Position: Inside staircase at eye level
  - Rotation: Face desired starting direction
  - Assign to ViewModeManager → Room View Anchor Staircase

### Step 3: Configure Birds-Eye Settings
- [ ] **Birds Eye Height**: `3.0` (meters above ground)
- [ ] **Birds Eye Distance**: `4.0` (meters from center)
- [ ] **Birds Eye Pitch Degrees**: `30` (downward angle)
- [ ] **Min Height**: `1.5` (prevents dropping into rooms)
- [ ] **Max Height**: `6.0` (prevents going too high)
- [ ] **Min Radius**: `2.0` (prevents too close)
- [ ] **Max Radius**: `8.0` (prevents too far)

### Step 4: Assign Locomotion Components
- [ ] **Room View Locomotion**: Drag `VRPlayerController` or your existing locomotion component
- [ ] **Birds Eye Locomotion**: Leave empty (auto-created on XR Origin)

### Step 5: Set Task Variant
- [ ] Select `ExperimentManager` GameObject
- [ ] In `ExperimentDataManager` component:
  - Set **Task Variant** to:
    - `BirdsEye_Corridor` for birds-eye corridor
    - `BirdsEye_Staircase` for birds-eye staircase
    - `RoomView_Corridor` for room view corridor
    - `RoomView_Staircase` for room view staircase

### Step 6: Test
- [ ] Press Play in Editor
- [ ] Check Console for ViewModeManager messages
- [ ] Verify spawn position:
  - BirdsEye: Above environment, looking down ~30°
  - RoomView: Inside environment at eye level
- [ ] Test movement:
  - BirdsEye: Left thumbstick moves around model
  - RoomView: Existing locomotion works

## Verification Points

### In Editor (Play Mode):
1. **Console Messages**:
   - Should see: `[ViewModeManager] BirdsEyeView setup complete` or `RoomView setup complete`
   - No errors about missing references

2. **XR Origin Position**:
   - BirdsEye: Should be above ground plane (Y = ground + height)
   - RoomView: Should be at anchor position or PlayerSpawn position

3. **XR Origin Rotation**:
   - BirdsEye: Should be looking down at environment center (~30° pitch)
   - RoomView: Should match anchor rotation

4. **Locomotion Components**:
   - BirdsEye: `BirdsEyeLocomotion` enabled on XR Origin
   - RoomView: `VRPlayerController` (or your locomotion) enabled

### On Quest (Build):
1. **Spawn Position**: User spawns in correct view mode
2. **Movement**: 
   - BirdsEye: Can move around model with thumbstick
   - RoomView: Existing locomotion works
3. **Constraints**: 
   - BirdsEye: Cannot drop below minHeight or go outside radius bounds
4. **Waypoint Placement**: Works normally in both views

## Integration Notes

### Existing Systems (No Changes Needed):
- ✅ `PointPlacementManager` - Works in both views
- ✅ `RayDepthController` - Works in both views
- ✅ `FlightPathManager` - Works in both views
- ✅ `PathRenderer` - Works in both views
- ✅ `ExperimentDataManager` - Automatically records view mode

### Coordination with PlayerSpawnPoint:
- `ViewModeManager` disables `PlayerSpawnPoint` in BirdsEyeView mode
- In RoomView mode, `PlayerSpawnPoint` can still work (or ViewModeManager handles it)
- No conflicts - ViewModeManager takes priority

## Troubleshooting

### Issue: User spawns in wrong position
**Solution**: 
- Check Ground Plane assignment
- Verify Environment Center points to correct GameObject
- Adjust Birds Eye Height/Distance settings
- Check Console for error messages

### Issue: Locomotion not working
**Solution**:
- Verify BirdsEyeLocomotion component exists on XR Origin
- Check it's enabled (should auto-enable in birds-eye mode)
- Verify left controller is connected
- Check thumbstick input in XR settings

### Issue: User can drop into rooms
**Solution**:
- Increase Min Height in ViewModeManager (e.g., to 2.0m)
- Verify Ground Plane Y position is correct
- Check that constraints are being applied (add debug logs if needed)

### Issue: ViewModeManager not found
**Solution**:
- Ensure ViewModeManager component is on ExperimentManager
- Ensure it's enabled
- Check Console for initialization messages

## Code Architecture

```
ViewModeManager (on ExperimentManager)
├── Reads TaskVariant from ExperimentDataManager
├── Determines ViewMode (RoomView vs BirdsEyeView)
├── Positions XR Origin
├── Configures Locomotion:
│   ├── RoomView → VRPlayerController (existing)
│   └── BirdsEyeView → BirdsEyeLocomotion (new)
└── Coordinates with PlayerSpawnPoint

BirdsEyeLocomotion (on XR Origin)
├── Reads left thumbstick input
├── Moves XR Origin on XZ plane
└── Applies constraints:
    ├── Height (min/max above ground)
    └── Radius (min/max from center)
```

## Next Steps

1. **Set up in Unity** following the checklist above
2. **Test in Editor** with different TaskVariants
3. **Build APK** for BirdsEye_Corridor condition
4. **Test on Quest** to verify spawn position and movement
5. **Adjust settings** (height, distance, constraints) as needed
6. **Build remaining APKs** for other conditions

## Questions or Issues?

Refer to `BIRDSEYE_VIEW_SETUP.md` for detailed setup instructions and troubleshooting.

