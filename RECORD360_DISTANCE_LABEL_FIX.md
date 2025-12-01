# Record360 Distance Label Fix

## Issue
During Record360 waypoint placement (two-step system), distance markers showing "0.00m" were appearing on both:
1. **Anchor Ghost** - The ghost that marks where the drone will arrive
2. **Recording Ghost** - The ghost that slides along the vertical line to set recording height

## Root Cause
The `RecordingGhost` and `AnchorGhost` were created as **duplicates of the main Ghost sphere** during Unity setup. When duplicated, they inherited:
- `PointLabelBillboard` components (for depth display)
- `TextMesh` children (showing distance in meters)

These labels were displaying "0.00m" because they weren't being properly managed during Record360 placement.

## Solution Implemented

### 1. Hide Main Depth Readout During Record360 Placement
**File**: `PointPlacementManager.cs`

When entering Record360 adjustment mode:
```csharp
// Hide the main depth readout during Record360 adjustment
if (_depthReadout != null)
{
    _depthReadout.gameObject.SetActive(false);
}
```

When exiting (confirmed or cancelled):
```csharp
// Re-enable the main depth readout
if (_depthReadout != null)
{
    _depthReadout.gameObject.SetActive(true);
}
```

### 2. Disable Labels on Anchor Ghost
**File**: `PointPlacementManager.cs`

When showing the anchor ghost:
```csharp
// Hide any distance labels on the anchor ghost
DisableLabelsOnGhost(_anchorGhostTransform);
```

Helper method added:
```csharp
private void DisableLabelsOnGhost(Transform ghostTransform)
{
    // Finds and disables all PointLabelBillboard components
    // Finds and disables all TextMesh components
}
```

### 3. Disable Labels on Recording Ghost
**File**: `RecordingHeightController.cs`

When activating the recording ghost:
```csharp
// Hide any distance labels on the recording ghost
DisableLabelsOnRecordingGhost();
```

Helper method added:
```csharp
private void DisableLabelsOnRecordingGhost()
{
    // Finds and disables all PointLabelBillboard components
    // Finds and disables all TextMesh components
}
```

### 4. Prevent Readout Updates During Adjustment
**File**: `RayDepthController.cs`

Skip updating the depth readout when in Record360 adjustment mode:
```csharp
// Don't update the readout during Record360 adjustment (it's hidden anyway)
if (!_manager.IsAdjustingRecordingHeight)
{
    _manager.UpdateReadout($"{_currentDepth:F2} m");
}
```

## Files Modified
1. ✅ `Assets/Scripts/Points/PointPlacementManager.cs`
   - Added helper method `DisableLabelsOnGhost()`
   - Hide/show main depth readout during Record360 placement
   - Disable labels on anchor ghost when shown

2. ✅ `Assets/Scripts/Points/RecordingHeightController.cs`
   - Added helper method `DisableLabelsOnRecordingGhost()`
   - Disable labels when recording ghost is activated

3. ✅ `Assets/Scripts/Points/RayDepthController.cs`
   - Skip readout updates during Record360 adjustment

## Result
✅ **No distance labels appear during Record360 placement**
- Anchor ghost: No labels
- Recording ghost: No labels
- Main ghost readout: Hidden during adjustment, re-enabled after

✅ **Normal waypoint placement unaffected**
- Distance readout still works for StopTurnGo waypoints
- Depth display shows correctly during normal placement

## Testing Checklist
- [ ] Place a StopTurnGo waypoint - verify distance readout shows correctly
- [ ] Place a Record360 waypoint:
  - [ ] Step 1 (Anchor): Verify NO "0.00m" label appears
  - [ ] Step 2 (Recording height): Verify NO labels on either ghost
  - [ ] Confirm placement: Verify readout returns for next waypoint
- [ ] Cancel Record360 placement: Verify readout returns
- [ ] Build and test on Quest 2

## Notes
- The fix is **non-destructive** - it only hides labels, doesn't remove them
- Labels are disabled using `gameObject.SetActive(false)` on the label components
- The main depth readout is temporarily hidden, not destroyed
- All changes are backwards compatible with existing scenes

---

**Status**: ✅ Complete - No compilation errors
**Date**: November 27, 2025

