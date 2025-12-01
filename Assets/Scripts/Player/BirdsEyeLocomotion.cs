using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Player
{
	/// <summary>
	/// Locomotion system for birds-eye/tabletop view.
	/// Allows user to move around the model from above, staying outside and above the geometry.
	/// </summary>
	[RequireComponent(typeof(XROrigin))]
	public class BirdsEyeLocomotion : MonoBehaviour
	{
		[Header("Movement Settings")]
		[SerializeField] private float _moveSpeed = 2.0f;
		
		[Header("Input")]
		[SerializeField] private XRNode _leftHandNode = XRNode.LeftHand;
		
		[Header("Constraints (Set by ViewModeManager)")]
		[SerializeField] private float _minHeight = 1.5f;
		[SerializeField] private float _maxHeight = 6.0f;
		[SerializeField] private float _minRadius = 2.0f;
		[SerializeField] private float _maxRadius = 8.0f;
		[SerializeField] private Transform _environmentCenter;
		
		[Header("Ground Reference")]
		[SerializeField] private Transform _groundPlane;
		
		private XROrigin _xrOrigin;
		private Camera _mainCamera;
		private InputDevice _leftHand;
		private float _groundY;
		
		private void Awake()
		{
			_xrOrigin = GetComponent<XROrigin>();
			_mainCamera = Camera.main;
			
			if (_groundPlane == null)
			{
				GameObject planeObj = GameObject.Find("Plane");
				if (planeObj != null)
				{
					_groundPlane = planeObj.transform;
				}
			}
			
			if (_groundPlane != null)
			{
				_groundY = _groundPlane.position.y;
			}
		}
		
		private void OnEnable()
		{
			_leftHand = InputDevices.GetDeviceAtXRNode(_leftHandNode);
			InputDevices.deviceConnected += OnDeviceConnected;
		}
		
		private void OnDisable()
		{
			InputDevices.deviceConnected -= OnDeviceConnected;
		}
		
		private void OnDeviceConnected(InputDevice device)
		{
			if ((device.characteristics & (InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller)) != 0)
			{
				_leftHand = device;
			}
		}
		
		private void Update()
		{
			if (_xrOrigin == null || _mainCamera == null) return;
			
			_leftHand = EnsureDevice(_leftHand, _leftHandNode);
			if (!_leftHand.isValid) return;
			
			// Get thumbstick input
			Vector2 thumbstick = Vector2.zero;
			if (_leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out thumbstick))
			{
				HandleMovement(thumbstick);
			}
		}
		
		private void HandleMovement(Vector2 input)
		{
			if (Mathf.Approximately(input.magnitude, 0f)) return;
			
			// Get camera's forward and right vectors (projected onto XZ plane)
			Vector3 cameraForward = Vector3.ProjectOnPlane(_mainCamera.transform.forward, Vector3.up).normalized;
			Vector3 cameraRight = Vector3.ProjectOnPlane(_mainCamera.transform.right, Vector3.up).normalized;
			
			// Calculate movement direction relative to camera
			Vector3 moveDirection = (cameraForward * input.y + cameraRight * input.x).normalized;
			
			// Apply movement
			Vector3 movement = moveDirection * _moveSpeed * Time.deltaTime;
			Vector3 newPosition = _xrOrigin.transform.position + movement;
			
			// Apply constraints
			newPosition = ApplyConstraints(newPosition);
			
			// Move the XR Origin
			_xrOrigin.transform.position = newPosition;
		}
		
		private Vector3 ApplyConstraints(Vector3 position)
		{
			// Height constraint: clamp Y between min and max above ground
			float groundLevel = _groundY;
			position.y = Mathf.Clamp(position.y, groundLevel + _minHeight, groundLevel + _maxHeight);
			
			// Radius constraint: keep distance from environment center within bounds
			if (_environmentCenter != null)
			{
				Vector3 centerPos = _environmentCenter.position;
				Vector3 horizontalOffset = new Vector3(position.x - centerPos.x, 0, position.z - centerPos.z);
				float currentRadius = horizontalOffset.magnitude;
				
				if (currentRadius < _minRadius)
				{
					// Too close: push outward
					horizontalOffset = horizontalOffset.normalized * _minRadius;
					position.x = centerPos.x + horizontalOffset.x;
					position.z = centerPos.z + horizontalOffset.z;
				}
				else if (currentRadius > _maxRadius)
				{
					// Too far: pull inward
					horizontalOffset = horizontalOffset.normalized * _maxRadius;
					position.x = centerPos.x + horizontalOffset.x;
					position.z = centerPos.z + horizontalOffset.z;
				}
			}
			
			return position;
		}
		
		/// <summary>
		/// Set constraint parameters (called by ViewModeManager).
		/// </summary>
		public void SetConstraints(float minHeight, float maxHeight, float minRadius, float maxRadius, Transform environmentCenter)
		{
			_minHeight = minHeight;
			_maxHeight = maxHeight;
			_minRadius = minRadius;
			_maxRadius = maxRadius;
			_environmentCenter = environmentCenter;
			
			if (_groundPlane != null)
			{
				_groundY = _groundPlane.position.y;
			}
		}
		
		private static InputDevice EnsureDevice(InputDevice device, XRNode node)
		{
			if (!device.isValid)
			{
				device = InputDevices.GetDeviceAtXRNode(node);
			}
			return device;
		}
	}
}

