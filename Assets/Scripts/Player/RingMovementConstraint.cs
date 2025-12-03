using Unity.XR.CoreUtils;
using UnityEngine;

namespace Player
{
	/// <summary>
	/// Constrains XR Origin movement to stay within a circular walkway ring.
	/// Clamps position to ring bounds in XZ and locks Y to ring floor height.
	/// </summary>
	[RequireComponent(typeof(XROrigin))]
	public class RingMovementConstraint : MonoBehaviour
	{
		[Header("Ring Reference")]
		[Tooltip("Name of the ring GameObject (default: 'Vollkörper_Addition')")]
		[SerializeField] private string _ringObjectName = "Vollkörper_Addition";
		
		[Tooltip("Parent GameObject name to search under (default: 'test ring')")]
		[SerializeField] private string _parentObjectName = "test ring";
		
		[Header("Constraint Settings")]
		[Tooltip("Enable constraint (can be toggled at runtime)")]
		[SerializeField] private bool _constraintEnabled = true;
		
		[Header("Debug")]
		[Tooltip("Draw debug gizmos in Scene view")]
		[SerializeField] private bool _drawGizmos = true;
		
		[Tooltip("Gizmo color for inner radius")]
		[SerializeField] private Color _innerRadiusColor = new Color(1f, 0f, 0f, 0.5f);
		
		[Tooltip("Gizmo color for outer radius")]
		[SerializeField] private Color _outerRadiusColor = new Color(0f, 1f, 0f, 0.5f);
		
		private XROrigin _xrOrigin;
		private GameObject _ringObject;
		private Bounds _ringBounds;
		private Vector3 _ringCenter;
		private float _ringFloorY;
		private float _innerRadius;
		private float _outerRadius;
		private bool _geometryCalculated = false;
		private VRPlayerController _vrPlayerController;
		private PlayerVerticalThruster _verticalThruster;
		
		private void Awake()
		{
			_xrOrigin = GetComponent<XROrigin>();
		}
		
		private void Start()
		{
			FindRingObject();
			
			if (_ringObject == null)
			{
				Debug.LogWarning($"[RingMovementConstraint] Ring object '{_ringObjectName}' not found. Constraints disabled.");
				return;
			}
			
			CalculateRingGeometry();
			
			// Disable vertical movement from other locomotion systems
			DisableVerticalMovement();
		}
		
		private void FindRingObject()
		{
			// First try to find under parent
			if (!string.IsNullOrEmpty(_parentObjectName))
			{
				GameObject parentObj = GameObject.Find(_parentObjectName);
				if (parentObj != null)
				{
					Transform ringTransform = parentObj.transform.Find(_ringObjectName);
					if (ringTransform != null)
					{
						_ringObject = ringTransform.gameObject;
						return;
					}
				}
			}
			
			// Fallback: search entire scene
			_ringObject = GameObject.Find(_ringObjectName);
		}
		
		private void CalculateRingGeometry()
		{
			if (_ringObject == null) return;
			
			// Try MeshRenderer bounds first
			MeshRenderer meshRenderer = _ringObject.GetComponent<MeshRenderer>();
			if (meshRenderer != null)
			{
				_ringBounds = meshRenderer.bounds;
			}
			else
			{
				// Fallback to Collider bounds
				Collider collider = _ringObject.GetComponent<Collider>();
				if (collider != null)
				{
					_ringBounds = collider.bounds;
				}
				else
				{
					Debug.LogError($"[RingMovementConstraint] Ring object '{_ringObjectName}' has no MeshRenderer or Collider!");
					return;
				}
			}
			
			// Calculate ring center in XZ (use bounds center)
			_ringCenter = new Vector3(_ringBounds.center.x, 0, _ringBounds.center.z);
			
			// Calculate ring floor height (use bounds min Y)
			_ringFloorY = _ringBounds.min.y;
			
			// Calculate inner and outer radius from bounds
			// For a ring, bounds.extents gives us the half-width in X and Z
			float maxExtent = Mathf.Max(_ringBounds.extents.x, _ringBounds.extents.z);
			
			// Estimate: inner radius is smaller, outer radius is larger
			// For a typical ring, inner radius might be ~50-70% of outer radius
			_outerRadius = maxExtent;
			_innerRadius = _outerRadius * 0.5f; // Conservative estimate - adjust if needed
			
			_geometryCalculated = true;
			
			Debug.Log($"[RingMovementConstraint] Ring geometry calculated: Center={_ringCenter}, FloorY={_ringFloorY}, InnerRadius={_innerRadius:F2}m, OuterRadius={_outerRadius:F2}m");
		}
		
		private void DisableVerticalMovement()
		{
			// Find and disable vertical movement in VRPlayerController
			_vrPlayerController = _xrOrigin.GetComponent<VRPlayerController>();
			if (_vrPlayerController == null)
			{
				_vrPlayerController = FindFirstObjectByType<VRPlayerController>();
			}
			
			if (_vrPlayerController != null)
			{
				_vrPlayerController.verticalSpeed = 0f; // Disable vertical movement
				Debug.Log("[RingMovementConstraint] Disabled vertical movement in VRPlayerController.");
			}
			
			// Find and disable PlayerVerticalThruster
			_verticalThruster = _xrOrigin.GetComponent<PlayerVerticalThruster>();
			if (_verticalThruster == null)
			{
				_verticalThruster = FindFirstObjectByType<PlayerVerticalThruster>();
			}
			
			if (_verticalThruster != null)
			{
				_verticalThruster.enabled = false; // Disable the component
				Debug.Log("[RingMovementConstraint] Disabled PlayerVerticalThruster.");
			}
		}
		
		private void FixedUpdate()
		{
			if (!_constraintEnabled || !_geometryCalculated || _xrOrigin == null) return;
			
			// Get current position
			Vector3 currentPos = _xrOrigin.transform.position;
			
			// Apply constraints
			Vector3 constrainedPos = ApplyConstraints(currentPos);
			
			// Always apply constraints (even if position seems same, force it)
			if (Vector3.Distance(currentPos, constrainedPos) > 0.001f)
			{
				_xrOrigin.transform.position = constrainedPos;
			}
		}
		
		private void LateUpdate()
		{
			// Also apply in LateUpdate as backup to catch any movement that happened after FixedUpdate
			if (!_constraintEnabled || !_geometryCalculated || _xrOrigin == null) return;
			
			// Get current position
			Vector3 currentPos = _xrOrigin.transform.position;
			
			// Apply constraints
			Vector3 constrainedPos = ApplyConstraints(currentPos);
			
			// Force apply the constrained position
			_xrOrigin.transform.position = constrainedPos;
		}
		
		private Vector3 ApplyConstraints(Vector3 position)
		{
			// Force Y to ring floor height (no tolerance - lock to floor)
			float targetY = _ringFloorY;
			
			// Get camera's local Y offset to maintain proper eye height
			if (_xrOrigin != null && _xrOrigin.Camera != null)
			{
				Transform cameraTransform = _xrOrigin.Camera.transform;
				if (_xrOrigin.CameraFloorOffsetObject != null)
				{
					cameraTransform = _xrOrigin.CameraFloorOffsetObject.transform;
				}
				
				Vector3 cameraLocalPos = _xrOrigin.transform.InverseTransformPoint(cameraTransform.position);
				float cameraLocalY = cameraLocalPos.y;
				
				// Adjust target Y to account for camera offset (so camera ends up at floor + eye height)
				targetY = _ringFloorY - cameraLocalY;
			}
			
			position.y = targetY;
			
			// Calculate distance from ring center in XZ plane
			Vector3 horizontalOffset = new Vector3(position.x - _ringCenter.x, 0, position.z - _ringCenter.z);
			float currentRadius = horizontalOffset.magnitude;
			
			// Clamp radius to stay within ring bounds
			if (currentRadius < _innerRadius)
			{
				// Too close to center: push outward to inner radius
				if (currentRadius > 0.01f) // Avoid division by zero
				{
					horizontalOffset = horizontalOffset.normalized * _innerRadius;
				}
				else
				{
					// At center, push in a default direction
					horizontalOffset = Vector3.forward * _innerRadius;
				}
				position.x = _ringCenter.x + horizontalOffset.x;
				position.z = _ringCenter.z + horizontalOffset.z;
			}
			else if (currentRadius > _outerRadius)
			{
				// Too far from center: pull inward to outer radius
				horizontalOffset = horizontalOffset.normalized * _outerRadius;
				position.x = _ringCenter.x + horizontalOffset.x;
				position.z = _ringCenter.z + horizontalOffset.z;
			}
			
			return position;
		}
		
		private void OnDrawGizmos()
		{
			if (!_drawGizmos || !_geometryCalculated) return;
			
			// Draw inner radius circle
			Gizmos.color = _innerRadiusColor;
			DrawCircle(_ringCenter, _innerRadius, _ringFloorY);
			
			// Draw outer radius circle
			Gizmos.color = _outerRadiusColor;
			DrawCircle(_ringCenter, _outerRadius, _ringFloorY);
			
			// Draw ring center marker
			Gizmos.color = Color.yellow;
			Gizmos.DrawSphere(new Vector3(_ringCenter.x, _ringFloorY, _ringCenter.z), 0.1f);
		}
		
		private void DrawCircle(Vector3 center, float radius, float y)
		{
			int segments = 32;
			float angleStep = 360f / segments;
			
			Vector3 prevPoint = center + new Vector3(radius, 0, 0);
			prevPoint.y = y;
			
			for (int i = 1; i <= segments; i++)
			{
				float angle = i * angleStep * Mathf.Deg2Rad;
				Vector3 nextPoint = center + new Vector3(
					Mathf.Cos(angle) * radius,
					0,
					Mathf.Sin(angle) * radius
				);
				nextPoint.y = y;
				
				Gizmos.DrawLine(prevPoint, nextPoint);
				prevPoint = nextPoint;
			}
		}
		
		/// <summary>
		/// Manually set ring geometry (useful if calculated values need adjustment).
		/// </summary>
		public void SetRingGeometry(Vector3 center, float floorY, float innerRadius, float outerRadius)
		{
			_ringCenter = center;
			_ringFloorY = floorY;
			_innerRadius = innerRadius;
			_outerRadius = outerRadius;
			_geometryCalculated = true;
		}
		
		/// <summary>
		/// Enable or disable the constraint at runtime.
		/// </summary>
		public void SetConstraintEnabled(bool enabled)
		{
			_constraintEnabled = enabled;
		}
	}
}

