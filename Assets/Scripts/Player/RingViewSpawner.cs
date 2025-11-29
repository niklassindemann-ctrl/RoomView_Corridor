using Unity.XR.CoreUtils;
using UnityEngine;

namespace Player
{
	/// <summary>
	/// Spawns the player on a circular walkway ring at runtime.
	/// Finds the Vollkörper_Addition object and positions the XR Origin on the ring floor.
	/// </summary>
	public class RingViewSpawner : MonoBehaviour
	{
		[Header("Ring Reference")]
		[Tooltip("Name of the ring GameObject (default: 'Vollkörper_Addition')")]
		[SerializeField] private string _ringObjectName = "Vollkörper_Addition";
		
		[Tooltip("Parent GameObject name to search under (default: 'test ring')")]
		[SerializeField] private string _parentObjectName = "test ring";
		
		[Header("Spawn Settings")]
		[Tooltip("Enable ring view spawning on Start")]
		[SerializeField] private bool _useRingViewOnStart = true;
		
		[Tooltip("Player eye height above ring floor (default: 1.6m)")]
		[SerializeField] private float _playerEyeHeight = 1.6f;
		
		[Tooltip("Spawn offset from ring center toward outer radius (0.0 = center, 1.0 = outer edge)")]
		[SerializeField] private float _spawnRadiusFactor = 0.75f;
		
		[Header("XR Setup")]
		[SerializeField] private XROrigin _xrOrigin;
		
		private GameObject _ringObject;
		private Bounds _ringBounds;
		private Vector3 _ringCenter;
		private float _ringFloorY;
		private float _innerRadius;
		private float _outerRadius;
		
		private void Awake()
		{
			if (_xrOrigin == null)
			{
				_xrOrigin = FindFirstObjectByType<XROrigin>();
			}
		}
		
		private void Start()
		{
			if (!_useRingViewOnStart) return;
			
			FindRingObject();
			
			if (_ringObject == null)
			{
				Debug.LogWarning($"[RingViewSpawner] Ring object '{_ringObjectName}' not found. Ring view spawning disabled.");
				return;
			}
			
			CalculateRingGeometry();
			StartCoroutine(SpawnOnRingAfterXRInit());
		}
		
		private void FindRingObject()
		{
			// First try to find under parent
			if (!string.IsNullOrEmpty(_parentObjectName))
			{
				GameObject parentObj = GameObject.Find(_parentObjectName);
				if (parentObj != null)
				{
					Debug.Log($"[RingViewSpawner] Found parent '{_parentObjectName}', searching for '{_ringObjectName}'...");
					Transform ringTransform = parentObj.transform.Find(_ringObjectName);
					if (ringTransform != null)
					{
						_ringObject = ringTransform.gameObject;
						Debug.Log($"[RingViewSpawner] Found ring object '{_ringObjectName}' under parent.");
						return;
					}
					else
					{
						Debug.LogWarning($"[RingViewSpawner] '{_ringObjectName}' not found under '{_parentObjectName}'. Searching entire scene...");
					}
				}
				else
				{
					Debug.LogWarning($"[RingViewSpawner] Parent '{_parentObjectName}' not found. Searching entire scene...");
				}
			}
			
			// Fallback: search entire scene
			_ringObject = GameObject.Find(_ringObjectName);
			if (_ringObject != null)
			{
				Debug.Log($"[RingViewSpawner] Found ring object '{_ringObjectName}' in scene.");
			}
			else
			{
				Debug.LogError($"[RingViewSpawner] Ring object '{_ringObjectName}' not found anywhere in scene!");
			}
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
					Debug.LogError($"[RingViewSpawner] Ring object '{_ringObjectName}' has no MeshRenderer or Collider!");
					return;
				}
			}
			
			// Calculate ring center in XZ (use bounds center)
			_ringCenter = new Vector3(_ringBounds.center.x, 0, _ringBounds.center.z);
			
			// Calculate ring floor height (use bounds min Y)
			_ringFloorY = _ringBounds.min.y;
			
			// Calculate inner and outer radius from bounds
			// For a ring, bounds.extents gives us the half-width in X and Z
			// We'll use the larger extent as the outer radius approximation
			float maxExtent = Mathf.Max(_ringBounds.extents.x, _ringBounds.extents.z);
			
			// Estimate: inner radius is smaller, outer radius is larger
			// For a typical ring, inner radius might be ~70% of outer radius
			// We'll use bounds to estimate, but this might need adjustment
			_outerRadius = maxExtent;
			_innerRadius = _outerRadius * 0.5f; // Conservative estimate - adjust if needed
			
			Debug.Log($"[RingViewSpawner] Ring geometry calculated: Center={_ringCenter}, FloorY={_ringFloorY}, InnerRadius={_innerRadius:F2}m, OuterRadius={_outerRadius:F2}m");
		}
		
		private System.Collections.IEnumerator SpawnOnRingAfterXRInit()
		{
			if (_xrOrigin == null)
			{
				Debug.LogError("[RingViewSpawner] XR Origin not found!");
				yield break;
			}
			
			// Wait for XR system to fully initialize (longer wait)
			for (int i = 0; i < 10; i++)
			{
				yield return null;
			}
			
			// Disable other spawn systems that might interfere
			var playerSpawnPoint = FindFirstObjectByType<PlayerSpawnPoint>();
			if (playerSpawnPoint != null)
			{
				playerSpawnPoint.enabled = false;
				Debug.Log("[RingViewSpawner] Disabled PlayerSpawnPoint to prevent conflicts.");
			}
			
			// Calculate spawn position on ring
			// Spawn at a point between inner and outer radius
			float spawnRadius = Mathf.Lerp(_innerRadius, _outerRadius, _spawnRadiusFactor);
			Vector3 spawnDirection = Vector3.forward; // Default: spawn facing forward
			Vector3 spawnPositionXZ = _ringCenter + spawnDirection * spawnRadius;
			
			// Calculate target camera position (ring floor + eye height)
			Vector3 targetCameraPos = new Vector3(spawnPositionXZ.x, _ringFloorY + _playerEyeHeight, spawnPositionXZ.z);
			
			// Get camera's local Y offset from origin
			Transform cameraTransform = _xrOrigin.Camera.transform;
			if (_xrOrigin.CameraFloorOffsetObject != null)
			{
				cameraTransform = _xrOrigin.CameraFloorOffsetObject.transform;
			}
			
			Vector3 cameraLocalPos = _xrOrigin.transform.InverseTransformPoint(cameraTransform.position);
			float cameraLocalY = cameraLocalPos.y;
			
			// Adjust target position to account for camera offset
			Vector3 targetOriginPos = targetCameraPos;
			targetOriginPos.y -= cameraLocalY;
			
			// Move the XR Origin directly (more reliable than MoveCameraToWorldLocation)
			_xrOrigin.transform.position = targetOriginPos;
			
			// Face the player toward the ring center
			Vector3 lookDirection = (_ringCenter - new Vector3(targetOriginPos.x, 0, targetOriginPos.z)).normalized;
			if (lookDirection.sqrMagnitude > 0.01f)
			{
				_xrOrigin.MatchOriginUpCameraForward(Vector3.up, lookDirection);
			}
			
			// Wait a frame and verify
			yield return null;
			
			// Force position again to ensure it stuck
			_xrOrigin.transform.position = targetOriginPos;
			
			Debug.Log($"[RingViewSpawner] Spawned player on ring at position: {targetOriginPos}, Ring floor: {_ringFloorY}");
		}
		
		/// <summary>
		/// Get the calculated ring geometry (for use by other scripts).
		/// </summary>
		public void GetRingGeometry(out Vector3 center, out float floorY, out float innerRadius, out float outerRadius)
		{
			center = _ringCenter;
			floorY = _ringFloorY;
			innerRadius = _innerRadius;
			outerRadius = _outerRadius;
		}
		
		/// <summary>
		/// Get the ring GameObject reference.
		/// </summary>
		public GameObject GetRingObject() => _ringObject;
	}
}

