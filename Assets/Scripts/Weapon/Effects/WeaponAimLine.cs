using UnityEngine;

public class WeaponAimLine : MonoBehaviour
{
    public LineRenderer line;
    public Transform weaponMuzzle;
    public float maxDistance = 50f;
    public LayerMask hitMask;

    private void Update()
    {
        DrawLaser();
    }

    private void DrawLaser()
    {
        Vector3 start = weaponMuzzle.position;
        Vector3 dir = weaponMuzzle.forward;     // ВАЖНО: всегда по forward
        dir.y = 0;                              // выравниваем по земле

        bool hit = Physics.Raycast(start, dir, out RaycastHit hitInfo, maxDistance, hitMask);

        Vector3 endPoint = hit ? hitInfo.point : start + dir * maxDistance;

        line.SetPosition(0, start);
        line.SetPosition(1, endPoint);
    }
}