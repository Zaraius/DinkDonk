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

    private Vector2 ballSpawnXRangeLeft  = new Vector2(-2.5f, 0f);
    private Vector2 ballSpawnXRangeRight = new Vector2(0f, 2.5f);
    private Vector2 targetXRangeLeft    = new Vector2(-2.25f, 0f);
    private Vector2 targetXRangeRight   = new Vector2(0f, 2.25f);
    private float lastDistanceToBall;
    private const float groundLevel = -0.3f;

    private Rigidbody rb;
    private Rigidbody ballRb;
    private Vector2 moveInput;
    private float paddleRotationInput;
    private int bounceCount = 0;
    private bool ballWellAboveGround = false;
    private bool ballInOpponentCourt = false;
    private bool ballWasInOpponentCourtLastFrame = false;
    private bool ballJustHit = false;
    private bool firstBounceChecked = false;
    private bool opponentSideBounceRewardGiven = false;
    private bool opponentSideReachedRewardGiven = false;
    private bool playerWasOnPlayerSideLastFrame = true;
    private float lastBallY = 0f;
    private int stuckFrameCount = 0;
    private const int stuckFrameThreshold = 5;
    private int currentEpisodeNumber = 0;

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

    public int GetCurrentEpisodeNumber() => currentEpisodeNumber;

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<Wall>(out Wall wall))
        {
            AddReward(-1f);
            EndEpisode();
        }
    }

    // ---------------------------------------------------------------
    // Called by Paddle.cs on ball contact
    // ---------------------------------------------------------------

    public void OnBallHit()
    {
        Vector3 ballPos = ballTransform.localPosition;

        // Penalty if hitting ball twice on player's side
        if (ballJustHit && ballPos.z < 0f)
        {
            AddReward(-20f);
            Debug.Log("Ball hit twice on player's side! -20 reward");
            EndEpisode();
            return;
        }

        AddReward(10f);
        ballJustHit        = true;
        firstBounceChecked = false;
    }

    // ---------------------------------------------------------------
    // ML-Agents: observations
    // ---------------------------------------------------------------

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.localPosition);     // 3
        sensor.AddObservation(ballTransform.localPosition); // 3
        sensor.AddObservation(ballRb.linearVelocity);       // 3
        // Total: 9 — Space Size unchanged
    }

    // ---------------------------------------------------------------
    // ML-Agents: actions + reward logic
    // ---------------------------------------------------------------

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX          = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ          = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float paddleRotation = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
        moveInput            = new Vector2(moveX, moveZ);
        paddleRotationInput  = paddleRotation;

        Vector3 ballPos = ballTransform.localPosition;

        // ── 1. Ball out of bounds ────────────────────────────────────
        // Context-aware: if we just hit it and it went out → our fault (-100).
        // If opponent hit it out → we win the point (+100).
        if (ballPos.y < -5f || Mathf.Abs(ballPos.x) > 5f || Mathf.Abs(ballPos.z) > 10f)
        {
            bool weHitItOut = ballJustHit;
            AddReward(weHitItOut ? -100f : 100f);
            EndEpisode();
            return;
        }

        // REMOVED: trajectory/height penalty — ball legitimately dips when
        // crossing the net. Out-of-bounds check above handles bad shots.

        // ── 2. Net-crossing penalty ───────────────────────────────────
        bool playerOnPlayerSide = transform.localPosition.z < 0f;
        if (playerWasOnPlayerSideLastFrame && !playerOnPlayerSide)
        {
            AddReward(-50f);
            Debug.Log("Player crossed the net! -50 reward");
            EndEpisode();
            return;
        }
        playerWasOnPlayerSideLastFrame = playerOnPlayerSide;

        // ── 3. Track which court the ball is in ───────────────────────
        ballInOpponentCourt = ballPos.z > 0f;

        if (ballInOpponentCourt != ballWasInOpponentCourtLastFrame)
        {
            bounceCount = 0;
            Debug.Log("Ball crossed half-court. Bounce count reset to 0");

            if (ballInOpponentCourt && ballJustHit && !opponentSideReachedRewardGiven)
            {
                opponentSideReachedRewardGiven = true;
                AddReward(15f);
            }
        }
        ballWasInOpponentCourtLastFrame = ballInOpponentCourt;

        // ── 4. Bounce detection (with hysteresis) ─────────────────────
        bool ballAtGround       = Mathf.Abs(ballPos.y - groundLevel) < 0.1f;
        bool ballAboveThreshold = ballPos.y > groundLevel + 0.3f;

        if (ballAtGround && ballWellAboveGround)
        {
            bounceCount++;
            ballWellAboveGround = false;
            Debug.Log($"Ground bounce: {bounceCount}");

            if (ballJustHit && !firstBounceChecked)
            {
                firstBounceChecked = true;
                bool withinXBounds = ballPos.x >= courtMinX && ballPos.x <= courtMaxX;
                bool withinZBounds = ballPos.z >= courtMinZ && ballPos.z <= courtMaxZ;

                // Good shot — landed in opponent's court, rally continues
                if (ballPos.z > 0f && withinXBounds && withinZBounds && !opponentSideBounceRewardGiven)
                {
                    opponentSideBounceRewardGiven = true;
                    AddReward(5f); // reduced from 50f — no EndEpisode, rally continues
                    Debug.Log("Ball landed on opponent's side! +5 reward");
                    ballJustHit = false;
                    return;
                }

                // First bounce out of bounds — fault
                if (!withinXBounds || !withinZBounds)
                {
                    AddReward(-100f); // raised from -15f, symmetric with win reward
                    Debug.Log("First bounce out of bounds! -100 reward");
                    EndEpisode();
                    return;
                }

                ballJustHit = false;
            }

            // Double bounce = failed to return = point lost
            // Changed from > 2 to > 1 — two bounces is already a fault in pickleball
            if (bounceCount > 1)
            {
                AddReward(-100f); // raised from -15f, symmetric with win reward
                Debug.Log("Double bounce — point lost! -100 reward");
                EndEpisode();
                return;
            }
        }

        if (ballAboveThreshold)
            ballWellAboveGround = true;

        // ── 5. Stuck-ball detection ───────────────────────────────────
        if (Mathf.Abs(ballPos.y - lastBallY) < 0.05f && ballPos.y < groundLevel + 0.1f)
        {
            stuckFrameCount++;
            if (stuckFrameCount > stuckFrameThreshold)
            {
                AddReward(-5f);
                Debug.Log($"Ball stuck on ground for {stuckFrameCount} frames");
                EndEpisode();
                return;
            }
        }
        else
        {
            stuckFrameCount = 0;
        }
        lastBallY = ballPos.y;

        // ── 6. Small shaping: move toward ball ───────────────────────
        float distanceToBall = Vector3.Distance(rb.position, ballTransform.position);
        if (distanceToBall < lastDistanceToBall)
            AddReward(0.05f);
        lastDistanceToBall = distanceToBall;
    }

    // ---------------------------------------------------------------
    // Unity physics update
    // ---------------------------------------------------------------

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
    // ML-Agents: episode reset
    // ---------------------------------------------------------------

    public override void OnEpisodeBegin()
    {
        currentEpisodeNumber++;
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        bool spawnLeft    = Random.value > 0.5f;
        Vector2 spawnRange  = spawnLeft ? ballSpawnXRangeLeft  : ballSpawnXRangeRight;
        Vector2 targetRange = spawnLeft ? targetXRangeRight    : targetXRangeLeft;
        Vector2 playerRange = spawnLeft ? targetXRangeRight    : targetXRangeLeft;

        transform.localPosition = new Vector3(
            Random.Range(playerRange.x, playerRange.y), 0.1f, Random.Range(-8.5f, -7.5f));
        rb.rotation = Quaternion.Euler(0f, 90f, 0f);

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

        moveInput                       = Vector2.zero;
        paddleRotationInput             = 0f;
        bounceCount                     = 0;
        ballWellAboveGround             = false;
        ballJustHit                     = false;
        firstBounceChecked              = false;
        opponentSideBounceRewardGiven   = false;
        opponentSideReachedRewardGiven  = false;
        ballInOpponentCourt             = false;
        ballWasInOpponentCourtLastFrame = false;
        playerWasOnPlayerSideLastFrame  = true;
        lastBallY                       = ballTransform.localPosition.y;
        stuckFrameCount                 = 0;

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