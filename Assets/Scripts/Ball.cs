using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Ball : MonoBehaviour
{
	private Rigidbody rb;

	private void Awake()
	{
		rb = GetComponent<Rigidbody>();
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
		rb.constraints = RigidbodyConstraints.FreezeRotation;
	}
}
