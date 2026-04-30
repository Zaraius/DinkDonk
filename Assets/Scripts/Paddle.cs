using UnityEngine;

public class Paddle : MonoBehaviour
{
    private HitBall hitBallAgent;
    [SerializeField] private float hitPowerMultiplier = 0.01f;
    [SerializeField] private float minHitSpeed = 0.5f;
    private const float maxUpwardForce = 0.65f;
    private const float maxTiltAngle = 45f;

    private int lastHitEpisode = -1;

    private void Start()
    {
        hitBallAgent = GetComponentInParent<HitBall>();
    }

    private float NormalizeAngle(float angle)
    {
        angle = angle % 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.TryGetComponent<Ball>(out _))
        {
            if (hitBallAgent == null)
            {
                return;
            }

            // Only process hits in the current episode, not from previous episodes
            int currentEpisode = hitBallAgent.GetCurrentEpisodeNumber();
            if (lastHitEpisode == currentEpisode)
            {
                return;
            }
            lastHitEpisode = currentEpisode;

            hitBallAgent.OnBallHit();

            Rigidbody ballRb = collision.rigidbody;
            if (ballRb != null)
            {
                // Get player movement direction (2D: X and Z)
                Rigidbody playerRb = hitBallAgent.GetComponent<Rigidbody>();
                Vector3 playerVelocity = playerRb != null ? playerRb.linearVelocity : Vector3.zero;

                // Horizontal direction from player movement
                Vector3 hitDirection = Vector3.zero;
                if (playerVelocity.magnitude > 0.01f)
                {
                    hitDirection.x = playerVelocity.x;
                    hitDirection.z = playerVelocity.z;
                    hitDirection = hitDirection.normalized;
                }
                else
                {
                    hitDirection = Vector3.forward;
                }

                // Get paddle tilt angle and map to upward force
                float paddleTilt = transform.localEulerAngles.z;
                paddleTilt = NormalizeAngle(paddleTilt);
                paddleTilt = Mathf.Clamp(paddleTilt, -maxTiltAngle, 0f);

                // Map -45 to 0 range to 0 to 0.65 force
                float normalizedTilt = Mathf.Abs(paddleTilt) / maxTiltAngle;
                float upwardForce = normalizedTilt * maxUpwardForce;

                // Apply speed
                float hitSpeed = Mathf.Max(playerVelocity.magnitude * hitPowerMultiplier, minHitSpeed);
                Vector3 hitForce = hitDirection * hitSpeed;

                // Add upward force mapped from paddle tilt
                hitForce.y += upwardForce;

                ballRb.AddForce(hitForce, ForceMode.Impulse);
                Debug.Log($"Ball hit with force: {hitForce}, frame: {Time.frameCount}");
            }
        }
    }

}
