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
    [SerializeField] private float ballServeForce = 15f;
    private Vector2 ballSpawnXRange = new Vector2(-2.3f, 2.3f);
    private Vector2 ballSpawnZRange = new Vector2(5.5f, 8.5f);
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
        transform.localPosition = new Vector3(Random.Range(-3f, 3f), 0.3f, Random.Range(-7f, -1f));
        rb.rotation = Quaternion.Euler(0f, 90f, 0f);

        Rigidbody ballRb = ballTransform.GetComponent<Rigidbody>();
        Vector3 ballStartPosition = new Vector3(
            Random.Range(ballSpawnXRange.x, ballSpawnXRange.y),
            1f,
            Random.Range(ballSpawnZRange.x, ballSpawnZRange.y)
        );

        Vector3 serveDirection = new Vector3(
            Random.Range(-0.35f, 0.35f),
            Random.Range(-0.45f, -0.15f),
            Random.Range(-1f, -0.65f)
        ).normalized;

        if (ballRb != null)
        {
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
            ballTransform.localPosition = ballStartPosition;
            ballTransform.localRotation = Quaternion.identity;
            ballRb.AddForce(serveDirection * ballServeForce, ForceMode.VelocityChange);
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