using UnityEngine;

public class ArrowMovement : MonoBehaviour
{
    public float moveSpeed = 0.5f;
    public float rotationSpeed = 10f;
    public float minY = 4f;
    public float maxY = 6f;

    private float initialY;

    void Start()
    {
        // Store starting local Y position
        initialY = transform.localPosition.y;
    }

    void Update()
    {
        // Vertical bobbing relative to the parent
        float verticalMovement = Mathf.Sin(Time.time * moveSpeed) * (maxY - minY) / 2f
                                 + (maxY + minY) / 2f;

        // Apply only local Y movement (stay above the moving target)
        Vector3 localPos = transform.localPosition;
        localPos.y = verticalMovement;
        transform.localPosition = localPos;

        // Rotate around Y axis, but force X = 0 and Z = 90
        float newY = transform.localEulerAngles.y + rotationSpeed * Time.deltaTime;
        transform.localRotation = Quaternion.Euler(0f, newY, 90f);
    }
}
