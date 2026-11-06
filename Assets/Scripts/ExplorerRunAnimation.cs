using UnityEngine;

// Procedural run cycle for the explorer model; no downloaded Animator asset is required.
public class ExplorerRunAnimation : MonoBehaviour
{
    public Transform leftArm, rightArm, leftLeg, rightLeg;
    public float runFrequency = 10f;
    public float swingAngle = 38f;
    public float bobAmount = 0.045f;
    Vector3 basePosition;
    void Start() { basePosition = transform.localPosition; }
    void Update()
    {
        GameManager gm = GameManager.Instance;
        bool active = gm == null || gm.State == GameState.Playing;
        if (!active) return;
        float phase = Time.time * runFrequency;
        float swing = Mathf.Sin(phase) * swingAngle;
        if (leftArm) leftArm.localRotation = Quaternion.Euler(swing, 0f, -8f);
        if (rightArm) rightArm.localRotation = Quaternion.Euler(-swing, 0f, 8f);
        if (leftLeg) leftLeg.localRotation = Quaternion.Euler(-swing * 0.9f, 0f, 0f);
        if (rightLeg) rightLeg.localRotation = Quaternion.Euler(swing * 0.9f, 0f, 0f);
        transform.localPosition = basePosition + Vector3.up * (Mathf.Abs(Mathf.Sin(phase * 2f)) * bobAmount);
    }
}
