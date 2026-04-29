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
    [SerializeField] private Transform opponentTransform;

    // Set true for Agent 1 (negative Z side), false for Agent 2 (positive Z side)
    [SerializeField] private bool isTeamOne = true;

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
    private bool ballInOpponentHalf = false;
    private bool ballWasInOpponentHalfLastFrame = false;
    private bool ballJustHit = false;
    private bool firstBounceChecked = false;
    private bool opponentSideBounceRewardGiven = false;
    private bool opponentSideReachedRewardGiven = false;
    private bool playerWasOnPlayerSideLastFrame = true;
    private bool ballTrajectoryChecked = false;
    private float maxBallHeightAfterHit = 0f;

    // ---------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        ballRb = ballTransform.GetComponent<Rigidbody>();
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

    // ---------------------------------------------------------------
    // Collision / trigger
    // ---------------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<Wall>(out Wall wall))
        {
            AddReward(-1f);
            EndEpisode();
        }
    }

    // ---------------------------------------------------------------
    // Called externally when paddle physically contacts the ball
    // ---------------------------------------------------------------

    public void OnBallHit()
    {
        AddReward(10f);
        ballJustHit = true;
        firstBounceChecked = false;
        ballTrajectoryChecked = false;
        maxBallHeightAfterHit = 0f;
        opponentSideBounceRewardGiven = false;
        opponentSideReachedRewardGiven = false;
    }

    // ---------------------------------------------------------------
    // ML-Agents: observations
    // ---------------------------------------------------------------

    public override void CollectObservations(VectorSensor sensor)
    {
        // Own state
        sensor.AddObservation(transform.localPosition);             // 3
        sensor.AddObservation(ballTransform.localPosition);         // 3
        sensor.AddObservation(ballRb.linearVelocity);               // 3

        // Opponent state
        sensor.AddObservation(opponentTransform.localPosition);     // 3

        // Relative opponent position (most useful for shot placement)
        Vector3 relativeOpponentPos = opponentTransform.localPosition - transform.localPosition;
        sensor.AddObservation(relativeOpponentPos);                 // 3

        // Paddle angle — agent is blind to its own DoF without this
        float currentPaddleAngle = NormalizeAngle(paddleTransform.localEulerAngles.z);
        sensor.AddObservation(currentPaddleAngle / 45f);            // 1  (normalized to [-1, 1])

        // Total: 16 observations
    }

    // ---------------------------------------------------------------
    // ML-Agents: actions + reward logic
    // ---------------------------------------------------------------

    public override void OnActionReceived(ActionBuffers actions) {
        float moveX     = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ     = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float paddleRot = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
        moveInput           = new Vector2(moveX, moveZ);
        paddleRotationInput = paddleRot;

        Vector3 ballPos = ballTransform.localPosition;

        // ── 1. Ball out of bounds — fault for whoever last hit it ────────
        if (ballPos.y < -5f || Mathf.Abs(ballPos.x) > 5f || Mathf.Abs(ballPos.z) > 10f)
        {
            // If ball is in opponent's half when it goes OOB, we hit it out — our fault
            // If ball is in our half when it goes OOB, opponent hit it out — we win point
            bool weHitItOut = ballJustHit;
            AddReward(weHitItOut ? -100f : 100f);
            EndEpisode();
            return;
        }

        // ── 2. Ball stuck / too slow after a hit ─────────────────────────
        if (ballRb != null)
        {
            float ballSpeed = ballRb.linearVelocity.magnitude;
            if (ballSpeed < 0.1f && ballJustHit)
            {
                AddReward(-5f);
                Debug.Log($"Ball too slow! Speed: {ballSpeed}");
                EndEpisode();
                return;
            }
            // REMOVED: trajectory penalty — ball legitimately dips crossing the net.
            // Out-of-bounds check above handles bad shots cleanly.
        }

        // ── 3. Net-crossing penalty (team-aware) ──────────────────────────
        bool playerOnOwnSide = isTeamOne
            ? transform.localPosition.z < 0f
            : transform.localPosition.z > 0f;

        if (playerWasOnPlayerSideLastFrame && !playerOnOwnSide)
        {
            AddReward(-50f);
            Debug.Log("Player crossed the net! -50");
            EndEpisode();
            return;
        }
        playerWasOnPlayerSideLastFrame = playerOnOwnSide;

        // ── 4. Track which half the ball is in (team-aware) ───────────────
        ballInOpponentHalf = isTeamOne ? ballPos.z > 0f : ballPos.z < 0f;

        // Ball just crossed into opponent's half
        if (ballInOpponentHalf && !ballWasInOpponentHalfLastFrame)
        {
            bounceCount = 0;

            // Small reward for getting the ball over the net — encourages rally length
            if (ballJustHit && !opponentSideReachedRewardGiven)
            {
                opponentSideReachedRewardGiven = true;
                AddReward(2f); // reduced from 15f — net crossing alone isn't a big deal
                Debug.Log("Ball crossed net into opponent's court! +2");
            }
        }

        // Ball just came back to our half — reset hit-tracking for next shot
        if (!ballInOpponentHalf && ballWasInOpponentHalfLastFrame)
        {
            bounceCount = 0;
            opponentSideBounceRewardGiven = false;
            opponentSideReachedRewardGiven = false;
            firstBounceChecked = false;
            ballJustHit = false;
        }

        ballWasInOpponentHalfLastFrame = ballInOpponentHalf;

        // ── 5. Bounce detection ───────────────────────────────────────────
        bool ballAtGround = Mathf.Abs(ballPos.y - groundLevel) < 0.1f;
        if (ballAtGround && ballWasAboveGroundLastFrame)
        {
            bounceCount++;

            if (ballJustHit && !firstBounceChecked)
            {
                firstBounceChecked = true;
                bool withinXBounds = ballPos.x >= courtMinX && ballPos.x <= courtMaxX;
                bool withinZBounds = ballPos.z >= courtMinZ && ballPos.z <= courtMaxZ;

                if (ballInOpponentHalf && withinXBounds && withinZBounds && !opponentSideBounceRewardGiven)
                {
                    opponentSideBounceRewardGiven = true;
                    AddReward(5f); // reduced from 50f — good shot but rally continues
                    Debug.Log("Ball landed in opponent's court! +5");
                    ballJustHit = false;
                    // No EndEpisode — rally continues
                    return;
                }

                if (!withinXBounds || !withinZBounds)
                {
                    AddReward(-25f); // out — we lose the point
                    Debug.Log("First bounce out of bounds! -25");
                    EndEpisode();
                    return;
                }

                ballJustHit = false;
            }

            // Double bounce on own side = we failed to return — opponent wins point
            if (bounceCount > 1 && !ballInOpponentHalf)
            {
                AddReward(-50f);
                Debug.Log("Double bounce on own side — point lost! -50");
                EndEpisode();
                return;
            }
        }

        ballWasAboveGroundLastFrame = ballPos.y > groundLevel;

        // ── 6. Small shaping: move toward ball BUT only when ball is on our side ──
        float distanceToBall = Vector3.Distance(rb.position, ballTransform.position);
        if (distanceToBall < lastDistanceToBall && !ballInOpponentHalf)
            AddReward(0.01f); // reduced from 0.05f, and gated to own side only
        lastDistanceToBall = distanceToBall;
    }

    // ---------------------------------------------------------------
    // ML-Agents: episode reset
    // ---------------------------------------------------------------

    public override void OnEpisodeBegin()
    {
        // Reset own physics
        if (!rb.isKinematic)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Spawn on correct side based on team
        bool spawnLeft   = Random.value > 0.5f;
        Vector2 spawnRange  = spawnLeft ? ballSpawnXRangeLeft  : ballSpawnXRangeRight;
        Vector2 targetRange = spawnLeft ? targetXRangeRight    : targetXRangeLeft;
        Vector2 playerRange = spawnLeft ? targetXRangeRight    : targetXRangeLeft;

        float spawnZ = isTeamOne
            ? Random.Range(-8.5f, -7.5f)
            : Random.Range(7.5f,   8.5f);

        transform.localPosition = new Vector3(
            Random.Range(playerRange.x, playerRange.y), 0.1f, spawnZ);
        rb.rotation = Quaternion.Euler(0f, isTeamOne ? 90f : -90f, 0f);

        // Only Team One owns the serve — Team Two just resets position
        if (isTeamOne)
        {
            Rigidbody bRb = ballTransform.GetComponent<Rigidbody>();

            Vector3 ballStartPosition = new Vector3(
                Random.Range(spawnRange.x, spawnRange.y), 1f, 8f);

            float targetX = Random.Range(targetRange.x, targetRange.y);
            float targetZ = Random.Range(-7.5f, -2.5f);
            Vector3 targetPosition = new Vector3(targetX, -0.3f, targetZ);

            float timeOfFlight = 1.2f;
            float g = Physics.gravity.y;

            float vy = (targetPosition.y - ballStartPosition.y
                        - 0.5f * g * timeOfFlight * timeOfFlight) / timeOfFlight;
            float vx = (targetPosition.x - ballStartPosition.x) / timeOfFlight;
            float vz = (targetPosition.z - ballStartPosition.z) / timeOfFlight;

            if (bRb != null)
            {
                bRb.linearVelocity  = Vector3.zero;
                bRb.angularVelocity = Vector3.zero;
                bRb.linearDamping   = 0f;
                bRb.angularDamping  = 0.05f;

                ballTransform.localPosition = ballStartPosition;
                ballTransform.localRotation = Quaternion.identity;

                bRb.linearVelocity = new Vector3(vx, vy, vz);
            }
            else
            {
                ballTransform.localPosition = ballStartPosition;
                ballTransform.localRotation = Quaternion.identity;
            }
        }

        // Reset all state flags
        moveInput                        = Vector2.zero;
        paddleRotationInput              = 0f;
        bounceCount                      = 0;
        ballWasAboveGroundLastFrame      = false;
        ballJustHit                      = false;
        firstBounceChecked               = false;
        ballTrajectoryChecked            = false;
        maxBallHeightAfterHit            = 0f;
        opponentSideBounceRewardGiven    = false;
        opponentSideReachedRewardGiven   = false;
        ballInOpponentHalf               = false;
        ballWasInOpponentHalfLastFrame   = false;
        playerWasOnPlayerSideLastFrame   = true;

        if (paddleTransform != null)
            paddleTransform.localRotation = Quaternion.identity;

        lastDistanceToBall = Vector3.Distance(transform.localPosition, ballTransform.localPosition);
    }

    // ---------------------------------------------------------------
    // ML-Agents: human control for testing
    // ---------------------------------------------------------------

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> ca = actionsOut.ContinuousActions;

        ca[0] = (Keyboard.current.dKey.isPressed ? 1f : 0f)
              - (Keyboard.current.aKey.isPressed ? 1f : 0f);

        ca[1] = (Keyboard.current.wKey.isPressed ? 1f : 0f)
              - (Keyboard.current.sKey.isPressed ? 1f : 0f);

        ca[2] = (Keyboard.current.downArrowKey.isPressed ? 1f : 0f)
              - (Keyboard.current.upArrowKey.isPressed   ? 1f : 0f);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private float NormalizeAngle(float angle)
    {
        angle = angle % 360f;
        if (angle >  180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }
}