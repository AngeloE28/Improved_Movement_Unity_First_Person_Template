using UnityEngine;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonControllerV2 : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;
		[Tooltip("Deceleration when there is no inputs")]
		public float DecelerationChangeRate = 5.0f;
		[Tooltip("Curve to control how the deceleration happens (Eg, Linear, Ease in, Ease out, Ease in and out)")]
		public AnimationCurve DecelerationCurve;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.4f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -20.0f;
		[Tooltip("Stronger gravity when jump apex is reached to fall faster. The engine default is -9.81f")]
		public float ApexGravity = -30.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.0f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;
		[Tooltip("Time left to jump when player is not grounded")]
		public float CoyoteTimer = 0.25f;
		[Tooltip("Time to buffer a jump input as soon as player touches the ground")]
		public float JumpBufferTimer = 0.15f;
		[Tooltip("Deceleration of the jump when shorthopping for variable jump heights")]
		public float JumpDecelerationChangeRate = 2.0f;
		[Tooltip("Curve to control how the deceleration happens (Eg, Linear, Ease in, Ease out, Ease in and out)")]
		public AnimationCurve jumpDecelerationCurve;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 89.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -89.0f;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;
		private float _currentGravity;
		private Vector3 _currentMoveInput;
		private bool _jumped;

		// timeout
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;
		private float _currentCoyoteTimer;
		private float _currentJumpBufferTimer;
		private float _jumpHeldTimer;


#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputsV2 _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputsV2>();
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;

			_currentGravity = Gravity;
			_currentCoyoteTimer = CoyoteTimer;			
		}

		private void Update()
		{
			JumpAndGravity();
			GroundedCheck();
			Move();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation()
		{
			// if there is an input
			if (_input.look.sqrMagnitude >= _threshold)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
				
				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// Update Cinemachine camera target pitch
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}
		}

		// Controls the lerp time (0-1) of the decelration when there is no inputs
		private float _inputPct;
		private void Move()
		{
			// set target speed based on move speed, sprint speed and if sprint is pressed
			float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

			// a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

			// note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is no input, set the target speed to 0
			if (_input.move == Vector2.zero) targetSpeed = 0.0f;

			// a reference to the players current horizontal velocity
			float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// accelerate or decelerate to target speed
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				// creates curved result rather than a linear one giving a more organic speed change
				// note T in Lerp is clamped, so we don't need to clamp our speed
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);

				// round speed to 3 decimal places
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

			// normalise input direction
			_currentMoveInput = _currentMoveInput.normalized;

			// note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is a move input rotate player when the player is moving
			if (_input.move != Vector2.zero)
			{
				// move
				_currentMoveInput = transform.right * _input.move.x + transform.forward * _input.move.y;
				_inputPct = 0.0f;
			}
            else
            {
				_inputPct = Mathf.MoveTowards(_inputPct, 1.0f, DecelerationChangeRate * Time.deltaTime); // 1 is the max value of a clamped lerp
				_currentMoveInput = Vector3.Lerp(_currentMoveInput, Vector3.zero, DecelerationCurve.Evaluate(_inputPct));
			}

			// move the player
			_controller.Move(_currentMoveInput.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
		}

		// Controls the lerp time (0-1) of the decelration when the jump is canceled for a shorter jump
		private float _jumpCancelPct;
		private void JumpAndGravity()
		{
			// Jump Buffer
			if (_input.jump)
			{
				_currentJumpBufferTimer = JumpBufferTimer;
				_jumpHeldTimer += Time.deltaTime;
			}
			else
			{
				_currentJumpBufferTimer -= Time.deltaTime;
				_jumpHeldTimer = 0.0f;
			}

			// Player can't keep holding the jump button to auto jump after landing
			float jumpHoldThreshold = 0.5f;
			if (_jumpHeldTimer > jumpHoldThreshold)
			{
				_input.jump = false;
			}

			if (Grounded)
			{
				// reset the timers and jump
				_fallTimeoutDelta = FallTimeout;
				_currentCoyoteTimer = CoyoteTimer;
				_jumpCancelPct = 0.0f;
				_jumped = false;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump
				if (_currentJumpBufferTimer >= 0.0f && _jumpTimeoutDelta <= 0.0f && !_jumped)
				{
					// the square root of H * -2 * G = how much velocity needed to reach desired height
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * _currentGravity);
					_jumped = true;
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// Coyote Time
				if (_currentCoyoteTimer >= 0.0f)
				{
					_currentCoyoteTimer -= Time.deltaTime;

					// Jump
					if (_input.jump && _jumpTimeoutDelta <= 0.0f && !_jumped)
					{
						// the square root of H * -2 * G = how much velocity needed to reach desired height
						_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * _currentGravity);
						_jumped = true;
					}
				}

				// Variable jump height
				if (!_input.jump && _verticalVelocity > 0.0f)
				{
					_jumpCancelPct = Mathf.MoveTowards(_jumpCancelPct, 1.0f, JumpDecelerationChangeRate * Time.deltaTime); // 1 is the max value of a clamped lerp
					_verticalVelocity = Mathf.Lerp(_verticalVelocity, 0.0f, jumpDecelerationCurve.Evaluate(_jumpCancelPct));
					_jumped = true;
				}


				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// Apex of jump is reached
				if (_verticalVelocity < 0)
				{
					// reset the jump timeout timer
					_jumpTimeoutDelta = JumpTimeout;
					_currentGravity = ApexGravity;
				}
				else
				{
					_currentGravity = Gravity;
				}

			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += _currentGravity * Time.deltaTime;
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}