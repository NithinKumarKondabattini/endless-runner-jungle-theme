using UnityEngine;

// Keeps the far-away jungle cliffs, temple and waterfalls ahead of the player.
public class BackdropFollow : MonoBehaviour
{
    public Transform target;

    void LateUpdate()
    {
        if (target == null) return;
        transform.position = new Vector3(0f, 0f, target.position.z);
    }
}
