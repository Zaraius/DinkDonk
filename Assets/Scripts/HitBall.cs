using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;
[RequireComponent(typeof(Rigidbody))]
public class HitBall : Agent
{
    private float heuristicSpeed = 3f;
    [SerializeField] private Transform ballTransform;
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float turnSpeed = 180f;
    private Vector2 ballSpawnXRangeLeft = new Vector2(-2.5f, 0f);
    private Vector2 ballSpawnXRangeRight = new Vector2(0f, 2.5f);
    private Vector2 targetXRangeLeft = new Vector2(-2.25f, 0f);
    private Vector2 targetXRangeRight = new Vector2(0f, 2.25f);
    private float lastDistanceToBall;

    private Rigidbody rb;
    private Vector2 moveInput;
    private float turnInput;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.TryGetComponent<Wall>(out Wall wall) )
        {
            SetReward(-1f);
            EndEpisode();
        }
    }

        private void OnCollisionEnter(Collision collision) 
    {
        // Use collision.gameObject or collision.collider to get the component
        if(collision.gameObject.TryGetComponent<Ball>(out Ball ball))
        {
            SetReward(10f);
            EndEpisode();
        }        
    }


    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(ballTransform.localPosition);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float turn = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
        moveInput = new Vector2(moveX, moveZ);
        turnInput = turn;

        float distanceToBall = Vector3.Distance(rb.position, ballTransform.position);
        if (distanceToBall < lastDistanceToBall)
        {
            AddReward(0.01f);
        }

        lastDistanceToBall = distanceToBall;
    }

    private void FixedUpdate()
    {
        Quaternion turnRotation = Quaternion.Euler(0f, turnInput * turnSpeed * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turnRotation);

        Vector3 move = (transform.right * moveInput.x + transform.forward * moveInput.y) * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
    }

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Randomly choose left->right or right->left
        bool spawnLeft = Random.value > 0.5f;
        Vector2 spawnRange = spawnLeft ? ballSpawnXRangeLeft : ballSpawnXRangeRight;
        Vector2 targetRange = spawnLeft ? targetXRangeRight : targetXRangeLeft;
        Vector2 playerRange = spawnLeft ? targetXRangeRight : targetXRangeLeft;

        // Player starts on the opposite half of ball spawn (diagonal)
        transform.localPosition = new Vector3(Random.Range(playerRange.x, playerRange.y), 0.3f, Random.Range(-8.5f, -7.5f));
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
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
            ballTransform.localPosition = ballStartPosition;
            ballTransform.localRotation = Quaternion.identity;
            ballRb.linearVelocity = serveVelocity;
        }
        else
        {
            ballTransform.localPosition = ballStartPosition;
            ballTransform.localRotation = Quaternion.identity;
        }

        moveInput = Vector2.zero;
        turnInput = 0f;
        lastDistanceToBall = Vector3.Distance(transform.localPosition, ballTransform.localPosition);
    }


    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> continuousActions = actionsOut.ContinuousActions;
        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
        {
            horizontal -= heuristicSpeed;
        }
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
        {
            horizontal += heuristicSpeed;
        }
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
        {
            vertical -= heuristicSpeed;
        }
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
        {
            vertical += heuristicSpeed;
        }

        continuousActions[0] = horizontal;
        continuousActions[1] = vertical;
        continuousActions[2] = 0f;
    }

    
    

}