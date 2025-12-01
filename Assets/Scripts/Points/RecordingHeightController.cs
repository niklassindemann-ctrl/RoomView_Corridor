using UnityEngine;

namespace Points
{
	/// <summary>
	/// Controls the visual guide and height selection for Record360 waypoints.
	/// Shows a vertical line from floor to ceiling and allows sliding a recording point.
	/// </summary>
	public class RecordingHeightController : MonoBehaviour
	{
		[Header("Visual Line Settings")]
		[SerializeField] private Color _lineColor = Color.grey;
		[SerializeField] private float _lineWidth = 0.005f; // 0.5cm thin line
		[SerializeField] private float _maxRaycastHeight = 50f; // Maximum height to raycast for ceiling
		[SerializeField] private float _defaultCeilingHeight = 4f; // Fallback if no ceiling found
		[SerializeField] private float _floorOffset = 0.1f; // Slight offset from floor

	[Header("Recording Point Settings")]
	[SerializeField] private Transform _recordingGhostTransform;
	[SerializeField] private Renderer _recordingGhostRenderer;

		[Header("References")]
		[SerializeField] private LayerMask _environmentLayer;

		private LineRenderer _verticalLine;
		private Vector3 _anchorPosition;
		private float _floorHeight;
		private float _ceilingHeight;
		private bool _isActive;

		/// <summary>
		/// Whether the recording height controller is currently active.
		/// </summary>
		public bool IsActive => _isActive;

		/// <summary>
		/// Current position of the recording point.
		/// </summary>
		public Vector3 RecordingPosition => _recordingGhostTransform != null ? _recordingGhostTransform.position : Vector3.zero;

		/// <summary>
		/// The anchor position where the vertical line starts.
		/// </summary>
		public Vector3 AnchorPosition => _anchorPosition;

		private void Awake()
		{
			CreateVerticalLine();
			SetActive(false);
		}

	/// <summary>
	/// Activate the recording height controller at a specific anchor position.
	/// </summary>
	public void ActivateAt(Vector3 anchorPosition)
	{
		_anchorPosition = anchorPosition;
		_isActive = true;

		// Find floor and ceiling
		FindFloorAndCeiling(_anchorPosition);

		// Position the vertical line
		UpdateVerticalLine();

		// Position recording ghost at anchor height initially and FORCEFULLY MAKE IT VISIBLE
		if (_recordingGhostTransform != null)
		{
			// Set position
			_recordingGhostTransform.position = _anchorPosition;
			
			// Enable the parent GameObject
			_recordingGhostTransform.gameObject.SetActive(true);
			
			// Enable all child renderers
			Renderer[] allRenderers = _recordingGhostTransform.GetComponentsInChildren<Renderer>(true);
			foreach (Renderer r in allRenderers)
			{
				r.enabled = true;
				r.gameObject.SetActive(true);
			}
			
			// Ensure the main renderer is visible
			if (_recordingGhostRenderer != null)
			{
				_recordingGhostRenderer.enabled = true;
				_recordingGhostRenderer.gameObject.SetActive(true);
			}
			
		// Hide any distance labels on the recording ghost (they're duplicated from main ghost)
		Debug.LogError("=== ATTEMPTING TO DISABLE RECORDING GHOST LABELS ===");
		DisableLabelsOnRecordingGhost();
		Debug.LogError("=== FINISHED DISABLING RECORDING GHOST LABELS ===");
		
		Debug.LogError($"RecordingHeightController: Recording ghost FORCEFULLY activated at {_anchorPosition}. Active={_recordingGhostTransform.gameObject.activeSelf}, RendererCount={allRenderers.Length}");
		}
		else
		{
			Debug.LogError("RecordingHeightController: _recordingGhostTransform is NULL!");
		}

		// Show vertical line
		if (_verticalLine != null)
		{
			_verticalLine.gameObject.SetActive(true);
		}

		// Update ghost color to bright red
		UpdateRecordingGhostColor();
	}

	/// <summary>
	/// Deactivate the recording height controller and hide visuals.
	/// NOTE: This only hides the vertical line and recording ghost, NOT the anchor ghost!
	/// </summary>
	public void SetActive(bool active)
	{
		_isActive = active;

		if (_verticalLine != null)
		{
			_verticalLine.gameObject.SetActive(active);
		}

		if (_recordingGhostTransform != null)
		{
			_recordingGhostTransform.gameObject.SetActive(active);
			
			// Also explicitly control renderer visibility
			if (_recordingGhostRenderer != null)
			{
				_recordingGhostRenderer.enabled = active;
			}
		}
		
		// DO NOT touch the anchor ghost - it's managed by PointPlacementManager
	}

	/// <summary>
	/// Update the recording point position based on ray hit.
	/// Constrains the point to slide along the vertical line.
	/// </summary>
	public void UpdateRecordingPointFromRay(Vector3 rayOrigin, Vector3 rayDirection)
	{
		if (!_isActive)
		{
			return;
		}

		if (_recordingGhostTransform == null)
		{
			Debug.LogError("RecordingHeightController: Recording ghost transform is null!");
			return;
		}

		// Ensure ghost is visible (in case it got hidden somehow)
		if (!_recordingGhostTransform.gameObject.activeSelf)
		{
			_recordingGhostTransform.gameObject.SetActive(true);
			Debug.LogWarning("RecordingHeightController: Recording ghost was hidden, re-enabling it!");
		}

		// Calculate the closest point on the vertical line to the ray
		Vector3 lineStart = new Vector3(_anchorPosition.x, _floorHeight, _anchorPosition.z);
		Vector3 lineEnd = new Vector3(_anchorPosition.x, _ceilingHeight, _anchorPosition.z);
		Vector3 lineDirection = Vector3.up;

		// Find intersection or closest point
		Vector3 closestPoint = ClosestPointOnLineToRay(lineStart, lineDirection, rayOrigin, rayDirection);

		// Clamp to floor and ceiling
		float clampedY = Mathf.Clamp(closestPoint.y, _floorHeight, _ceilingHeight);
		Vector3 constrainedPosition = new Vector3(_anchorPosition.x, clampedY, _anchorPosition.z);

		_recordingGhostTransform.position = constrainedPosition;
		
		// Debug logging (can be removed later)
		if (Time.frameCount % 30 == 0) // Log every 30 frames to avoid spam
		{
			Debug.Log($"RecordingHeightController: Ghost at Y={clampedY:F2} (floor={_floorHeight:F2}, ceiling={_ceilingHeight:F2}), Visible={_recordingGhostTransform.gameObject.activeSelf}");
		}
	}

		/// <summary>
		/// Update the recording point position directly from a depth value.
		/// Used when the user adjusts depth with controller stick.
		/// </summary>
		public void UpdateRecordingPointHeight(float targetY)
		{
			if (!_isActive || _recordingGhostTransform == null) return;

			// Clamp to floor and ceiling
			float clampedY = Mathf.Clamp(targetY, _floorHeight, _ceilingHeight);
			Vector3 constrainedPosition = new Vector3(_anchorPosition.x, clampedY, _anchorPosition.z);

			_recordingGhostTransform.position = constrainedPosition;
		}

		/// <summary>
		/// Find the floor and ceiling heights at the anchor position using raycasts.
		/// </summary>
		private void FindFloorAndCeiling(Vector3 position)
		{
			// Raycast down to find floor
			RaycastHit floorHit;
			if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out floorHit, 100f, _environmentLayer))
			{
				_floorHeight = floorHit.point.y + _floorOffset;
			}
			else
			{
				_floorHeight = 0f + _floorOffset;
			}

			// Raycast up to find ceiling
			RaycastHit ceilingHit;
			if (Physics.Raycast(position, Vector3.up, out ceilingHit, _maxRaycastHeight, _environmentLayer))
			{
				_ceilingHeight = ceilingHit.point.y - _floorOffset;
			}
			else
			{
				_ceilingHeight = position.y + _defaultCeilingHeight;
			}

			// Ensure ceiling is above floor
			if (_ceilingHeight <= _floorHeight)
			{
				_ceilingHeight = _floorHeight + _defaultCeilingHeight;
			}

			Debug.Log($"RecordingHeightController: Floor={_floorHeight:F2}m, Ceiling={_ceilingHeight:F2}m, Range={_ceilingHeight - _floorHeight:F2}m");
		}

		/// <summary>
		/// Create the vertical line renderer.
		/// </summary>
		private void CreateVerticalLine()
		{
			if (_verticalLine != null) return;

			GameObject lineObj = new GameObject("Recording Height Guide Line");
			lineObj.transform.SetParent(transform);
			_verticalLine = lineObj.AddComponent<LineRenderer>();

			// Configure line renderer
			_verticalLine.useWorldSpace = true;
			_verticalLine.startWidth = _lineWidth;
			_verticalLine.endWidth = _lineWidth;
			_verticalLine.positionCount = 2;
			_verticalLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_verticalLine.receiveShadows = false;

			// Create material
			Material lineMat = new Material(Shader.Find("Sprites/Default"));
			lineMat.color = _lineColor;
			_verticalLine.material = lineMat;

			_verticalLine.gameObject.SetActive(false);
		}

		/// <summary>
		/// Update the vertical line positions from floor to ceiling.
		/// </summary>
		private void UpdateVerticalLine()
		{
			if (_verticalLine == null) return;

			Vector3 lineStart = new Vector3(_anchorPosition.x, _floorHeight, _anchorPosition.z);
			Vector3 lineEnd = new Vector3(_anchorPosition.x, _ceilingHeight, _anchorPosition.z);

			_verticalLine.SetPosition(0, lineStart);
			_verticalLine.SetPosition(1, lineEnd);
		}

		/// <summary>
		/// Update the recording ghost color to match Record360 type.
		/// </summary>
		private void UpdateRecordingGhostColor()
		{
			if (_recordingGhostRenderer == null) return;

			Color recordColor = WaypointTypeDefinition.GetTypeColor(WaypointType.Record360);
			foreach (var mat in _recordingGhostRenderer.sharedMaterials)
			{
				if (mat != null && mat.HasProperty("_Color"))
				{
					mat.color = recordColor;
				}
			}
		}

	/// <summary>
	/// Calculate the closest point on a vertical line to a ray.
	/// Uses a simpler approach: find where the ray passes closest to the vertical line.
	/// </summary>
	private Vector3 ClosestPointOnLineToRay(Vector3 linePoint, Vector3 lineDir, Vector3 rayPoint, Vector3 rayDir)
	{
		// For a vertical line through anchor position, we want to find the height
		// where the ray passes closest to the line
		
		// The vertical line is at (anchorX, Y, anchorZ) for any Y
		// We want to find the Y value where the ray is closest to this vertical line
		
		// Project the ray onto the XZ plane to find the closest horizontal approach
		Vector3 rayOnXZ = new Vector3(rayDir.x, 0, rayDir.z);
		Vector3 rayOriginOnXZ = new Vector3(rayPoint.x, 0, rayPoint.z);
		Vector3 anchorOnXZ = new Vector3(_anchorPosition.x, 0, _anchorPosition.z);
		
		// Find parametric t where ray is closest to anchor in XZ plane
		Vector3 toAnchor = anchorOnXZ - rayOriginOnXZ;
		float rayLength = rayOnXZ.sqrMagnitude;
		
		float t;
		if (rayLength > 0.0001f)
		{
			t = Vector3.Dot(toAnchor, rayOnXZ) / rayLength;
		}
		else
		{
			// Ray pointing straight up/down - use current position
			t = 0f;
		}
		
		// Clamp t to reasonable range (don't go behind the controller)
		t = Mathf.Max(0f, t);
		
		// Calculate the 3D point on the ray at this parameter
		Vector3 closestPointOnRay = rayPoint + rayDir * t;
		
		// The Y coordinate of this point is where we want the recording ghost
		float targetY = closestPointOnRay.y;
		
		// Return a point on the vertical line at this height
		return new Vector3(_anchorPosition.x, targetY, _anchorPosition.z);
	}

		/// <summary>
		/// Set the recording ghost transform (called from PointPlacementManager).
		/// </summary>
		public void SetRecordingGhostTransform(Transform ghostTransform, Renderer ghostRenderer)
		{
			_recordingGhostTransform = ghostTransform;
			_recordingGhostRenderer = ghostRenderer;
		}

		/// <summary>
		/// Set the environment layer for raycasting.
		/// </summary>
		public void SetEnvironmentLayer(LayerMask layer)
		{
			_environmentLayer = layer;
		}
		
	/// <summary>
	/// Disable any PointLabelBillboard or TextMesh components on the recording ghost.
	/// The recording ghost is a duplicate of the main ghost, so it inherits distance labels.
	/// FIXED: Disables the "Depth Readout" child GameObject by name.
	/// </summary>
	private void DisableLabelsOnRecordingGhost()
	{
		if (_recordingGhostTransform == null)
		{
			Debug.LogError("DisableLabelsOnRecordingGhost: _recordingGhostTransform is NULL!");
			return;
		}
		
		Debug.LogError($"RecordingHeightController: Scanning {_recordingGhostTransform.name} for labels...");
		
		// SOLUTION: Find and disable the "Depth Readout" child by name
		Transform depthReadoutChild = _recordingGhostTransform.Find("Depth Readout");
		if (depthReadoutChild != null)
		{
			depthReadoutChild.gameObject.SetActive(false);
			Debug.LogError($"✅ DISABLED 'Depth Readout' child on {_recordingGhostTransform.name}!");
		}
		else
		{
			Debug.LogError($"⚠️ No 'Depth Readout' child found on {_recordingGhostTransform.name}");
		}
	}
	}
}

