using UnityEngine;

public class magnet : MonoBehaviour
{
    public float pullForce;

    private void OnTriggerStay(Collider other)
    {
        BallForce myBallForces = other.gameObject.GetComponent<BallForce>();
        if (myBallForces != null)
        {
            Vector3 forceVolocity = transform.position - other.transform.position;
            other.GetComponent<Rigidbody>().AddForce(forceVolocity * pullForce, ForceMode.Impulse);
        }
    }
    
}
