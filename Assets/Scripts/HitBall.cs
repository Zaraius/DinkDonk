using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;
[RequireComponent(typeof(Rigidbody))]
public class HitBall : Agent
{
    [SerializeField] private Transform ballTransform;
    [SerializeField] private Transform paddleTransform;
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float paddleRotationSpeed = 90f;

    // Court boundaries
    private const float courtMinX = -2.5f;
    private const float courtMaxX = 2.5f;
    private const float courtMinZ = -6f;
    private const float courtMaxZ = 6f;

    private Vector2 ballSpawnXRangeLeft = new Vector2(-2.5f, 0f);
    private Vector2 ballSpawnXRangeRight = new Vector2(0f, 2.5f);
    private Vector2 targetXRangeLeft = new Vector2(-2.25f, 0f);
    private Vector2 targetXRangeRight = new Vector2(0f, 2.25f);
    private float lastDistanceToBall;
    private const float groundLevel = -0.3f;

    private Rigidbody rb;
    private Rigidbody ballRb;
    private Vector2 moveInput;
    private float paddleRotationInput;
    private int bounceCount = 0;
    private bool ballWasAboveGroundLastFrame = false;
    private bool ballInOpponentCourt = false;
    private bool ballWasInOpponentCourtLastFrame = false;
    private bool ballJustHit = false;
    private bool firstBounceChecked = false;
    private bool opponentSideBounceRewardGiven = false;
    private bool opponentSideReachedRewardGiven = false;
    private bool playerWasOnPlayerSideLastFrame = true;


    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        ballRb = ballTransform.GetComponent<Rigidbody>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.TryGetComponent<Wall>(out Wall wall) )
        {
            SetReward(-1f);
            EndEpisode();
        }
    }

    public void OnBallHit()
    {
        SetReward(10f);
        ballJustHit = true;
        firstBounceChecked = false;
    }


    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(ballTransform.localPosition);
        sensor.AddObservation(ballRb.linearVelocity);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float paddleRotation = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
        moveInput = new Vector2(moveX, moveZ);
        paddleRotationInput = paddleRotation;

        // Check if ball hit a wall (went out of bounds)
        Vector3 ballPos = ballTransform.localPosition;
        if (ballPos.y < -5f || Mathf.Abs(ballPos.x) > 5f || Mathf.Abs(ballPos.z) > 10f)
        {
            SetReward(-10f);
            EndEpisode();
            return;
        }

        // Check if ball is nearly stationary (stuck or not moving)
        if (ballRb != null)
        {
            float ballSpeed = ballRb.linearVelocity.magnitude;
            if (ballSpeed < 0.1f && ballJustHit)
            {
                SetReward(-2f);
                Debug.Log($"Ball too slow! Speed: {ballSpeed}");
                EndEpisode();
                return;
            }
        }

        // Track which court the ball is in
        ballInOpponentCourt = ballPos.z > 0f;

        // Check if player crossed the midline (z=0)
        bool playerOnPlayerSide = transform.localPosition.z < 0f;
        if (playerWasOnPlayerSideLastFrame && !playerOnPlayerSide)
        {
            SetReward(-50f);
            Debug.Log($"Player crossed the net! -50 reward");
            EndEpisode();
            return;
        }
        playerWasOnPlayerSideLastFrame = playerOnPlayerSide;

        // Reset bounce count when ball crosses half-court
        if (ballInOpponentCourt != ballWasInOpponentCourtLastFrame)
        {
            bounceCount = 0;
            // Debug.Log($"Ball crossed half-court. Bounce count reset to 0");

            // Reward for reaching opponent's side after a hit
            if (ballInOpponentCourt && ballJustHit && !opponentSideReachedRewardGiven)
            {
                opponentSideReachedRewardGiven = true;
                SetReward(15f);
                Debug.Log($"Ball reached opponent's court! +15 reward");
            }
        }
        ballWasInOpponentCourtLastFrame = ballInOpponentCourt;

        // Debug.Log("y: " + ballPos.y);
        // Debug.Log("ballWasAboveGroundLastFrame: " + ballWasAboveGroundLastFrame);
        // Debug.Log("ballAtGround: " + (Mathf.Abs(ballPos.y - groundLevel) < 0.1f));
        // Detect ground bounce (ball at Y = -0.3)
        bool ballAtGround = Mathf.Abs(ballPos.y - groundLevel) < 0.1f;
        if (ballAtGround && ballWasAboveGroundLastFrame)
        {
            bounceCount++;
            // Debug.Log($"Ground bounce: {bounceCount}");

            // Check first bounce bounds after paddle hit
            if (ballJustHit && !firstBounceChecked)
            {
                firstBounceChecked = true;
                bool withinXBounds = ballPos.x >= courtMinX && ballPos.x <= courtMaxX;
                bool withinZBounds = ballPos.z >= courtMinZ && ballPos.z <= courtMaxZ;

                if (!withinXBounds || !withinZBounds)
                {
                    // Debug.Log($"First bounce out of bounds! X: {ballPos.x}, Z: {ballPos.z}");
                    SetReward(-15f);
                    EndEpisode();
                    return;
                }
                ballJustHit = false;
            }

            // Reward if ball bounces on opponent's side in bounds after return
            if (ballJustHit && ballPos.z > 0f && !opponentSideBounceRewardGiven)
            {
                bool withinXBounds = ballPos.x >= courtMinX && ballPos.x <= courtMaxX;
                bool withinZBounds = ballPos.z <= courtMaxZ;

                if (withinXBounds && withinZBounds)
                {
                    opponentSideBounceRewardGiven = true;
                    SetReward(50f);
                    Debug.Log($"Ball landed on opponent's side! +50 reward");
                    ballJustHit = false;
                    EndEpisode();
                }
            }

            // Penalty if too many bounces on player's side
            if (bounceCount > 2)
            {
                SetReward(-2f);
                EndEpisode();
                return;
            }
        }

        // Update ball height tracking
        ballWasAboveGroundLastFrame = ballPos.y > groundLevel;

        float distanceToBall = Vector3.Distance(rb.position, ballTransform.position);
        if (distanceToBall < lastDistanceToBall)
        {
            AddReward(0.05f);
        }

        lastDistanceToBall = distanceToBall;
    }

    private void FixedUpdate()
    {
        Vector3 move = new Vector3(moveInput.x, 0, moveInput.y) * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);

        if (paddleTransform != null)
        {
            Vector3 currentEuler = paddleTransform.localEulerAngles;
            float newZRotation = currentEuler.z + paddleRotationInput * paddleRotationSpeed * Time.fixedDeltaTime;
            newZRotation = NormalizeAngle(newZRotation);
            newZRotation = Mathf.Clamp(newZRotation, -45f, 0f);
            paddleTransform.localEulerAngles = new Vector3(currentEuler.x, currentEuler.y, newZRotation);
        }
    }

    private float NormalizeAngle(float angle)
    {
        angle = angle % 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    public override void OnEpisodeBegin()
    {
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Randomly choose left->right or right->left
        bool spawnLeft = Random.value > 0.5f;
        Vector2 spawnRange = spawnLeft ? ballSpawnXRangeLeft : ballSpawnXRangeRight;
        Vector2 targetRange = spawnLeft ? targetXRangeRight : targetXRangeLeft;
        Vector2 playerRange = spawnLeft ? targetXRangeRight : targetXRangeLeft;

        // Player starts on the opposite half of ball spawn (diagonal)
        transform.localPosition = new Vector3(Random.Range(playerRange.x, playerRange.y), 0.1f, Random.Range(-8.5f, -7.5f));
        rb.rotation = Quaternion.Euler(0f, 90f, 0f);

        Rigidbody ballRb = ballTransform.GetComponent<Rigidbody>();

        Vector3 ballStartPosition = new Vector3(
            Random.Range(spawnRange.x, spawnRange.y),
            1f,
            8f
        );

        // Target landing zone: z: -7.5 to -2.5, y: -0.3
        float targetX = Random.Range(targetRange.x, targetRange.y);
        float targetZ = Random.Range(-7.5f, -2.5f);
        float targetY = -0.3f;
        Vector3 targetPosition = new Vector3(targetX, targetY, targetZ);

        // Calculate required velocity for projectile motion
        float timeOfFlight = 1.2f;
        float g = Physics.gravity.y;

        // Vertical velocity needed to reach target Y at timeOfFlight
        float vy = (targetPosition.y - ballStartPosition.y - 0.5f * g * timeOfFlight * timeOfFlight) / timeOfFlight;

        // Horizontal velocities
        float vx = (targetPosition.x - ballStartPosition.x) / timeOfFlight;
        float vz = (targetPosition.z - ballStartPosition.z) / timeOfFlight;

        Vector3 serveVelocity = new Vector3(vx, vy, vz);

        if (ballRb != null)
        {
            // Reset all ball physics and state
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
            ballRb.linearDamping = 0f;
            ballRb.angularDamping = 0.05f;

            // Reset position and rotation
            ballTransform.localPosition = ballStartPosition;
            ballTransform.localRotation = Quaternion.identity;

            // Explicitly reset Y velocity/force
            Vector3 resetVelocity = ballRb.linearVelocity;
            resetVelocity.y = 0f;
            ballRb.linearVelocity = resetVelocity;

            // Set serve velocity
            ballRb.linearVelocity = serveVelocity;
        }
        else
        {
            ballTransform.localPosition = ballStartPosition;
            ballTransform.localRotation = Quaternion.identity;
        }

        moveInput = Vector2.zero;
        paddleRotationInput = 0f;
        bounceCount = 0;
        ballWasAboveGroundLastFrame = false;
        ballJustHit = false;
        firstBounceChecked = false;
        opponentSideBounceRewardGiven = false;
        opponentSideReachedRewardGiven = false;

        if (paddleTransform != null)
        {
            paddleTransform.localRotation = Quaternion.identity;
        }

        lastDistanceToBall = Vector3.Distance(transform.localPosition, ballTransform.localPosition);
    }


    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> continuousActions = actionsOut.ContinuousActions;
        float horizontal = 0f;
        float vertical = 0f;
        float paddleRotation = 0f;

        if (Keyboard.current.aKey.isPressed)
        {
            horizontal -= moveSpeed;
        }
        if (Keyboard.current.dKey.isPressed)
        {
            horizontal += moveSpeed;
        }
        if (Keyboard.current.sKey.isPressed)
        {
            vertical -= moveSpeed;
        }
        if (Keyboard.current.wKey.isPressed)
        {
            vertical += moveSpeed;
        }
        if (Keyboard.current.upArrowKey.isPressed)
        {
            paddleRotation -= paddleRotationSpeed;
        }
        if (Keyboard.current.downArrowKey.isPressed)
        {
            paddleRotation += paddleRotationSpeed;
        }

        continuousActions[0] = horizontal;
        continuousActions[1] = vertical;
        continuousActions[2] = paddleRotation;
    }

    
    

}