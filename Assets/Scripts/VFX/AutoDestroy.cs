using UnityEngine;

public class AutoDestroy : MonoBehaviour
{
    public float delay = 0.5f;

    void Start()
    {
        Destroy(gameObject, delay);
    }
}