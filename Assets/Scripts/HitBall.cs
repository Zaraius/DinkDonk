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
    [SerializeField] private float moveSpeed = 3f;
    private float lastDistanceToBall;

    private Rigidbody rb;
    private Vector2 moveInput;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
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
        moveInput = new Vector2(moveX, moveZ);

        float distanceToBall = Vector3.Distance(rb.position, ballTransform.position);
        if (distanceToBall < lastDistanceToBall)
        {
            AddReward(0.01f);
        }

        lastDistanceToBall = distanceToBall;
    }

    private void FixedUpdate()
    {
        Vector3 move = new Vector3(moveInput.x, 0f, moveInput.y) * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
    }

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.localPosition = new Vector3(Random.Range(-3f, 3f), 1f, Random.Range(-7f, -1f));
        transform.localRotation = Quaternion.identity;

        Rigidbody ballRb = ballTransform.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
            ballTransform.localPosition = new Vector3(Random.Range(-3f, 3f), 1f, Random.Range(-7f, -1f));
            ballTransform.localRotation = Quaternion.identity;
        }
        else
        {
            ballTransform.localPosition = new Vector3(Random.Range(-3f, 3f), 1f, Random.Range(-7f, -1f));
            ballTransform.localRotation = Quaternion.identity;
        }

        moveInput = Vector2.zero;
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
    }

    
    

}