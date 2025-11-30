using UnityEngine;

public static class SpawnUtils
{
    // Projects world point down onto the surface below. Tries the provided layerMask first (e.g. Road),
    // then falls back to any collider if nothing was hit. Returns adjusted position (same X/Z, surface Y).
    public static Vector3 ProjectOntoSurface(Vector3 worldPoint, float upOffset = 2f, float maxDown = 25f, LayerMask? layerMask = null)
    {
        Vector3 origin = worldPoint + Vector3.up * upOffset;

        // try provided mask or common "Road" layer
        LayerMask mask = layerMask ?? (LayerMask.GetMask("RoadSurface"));
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, upOffset + maxDown, mask.value, QueryTriggerInteraction.Ignore))
        {
            return new Vector3(worldPoint.x, hit.point.y, worldPoint.z);
        }

        // fallback to any collider
        if (Physics.Raycast(origin, Vector3.down, out hit, upOffset + maxDown, ~0, QueryTriggerInteraction.Ignore))
        {
            return new Vector3(worldPoint.x, hit.point.y, worldPoint.z);
        }

        // nothing found: return original (caller should decide fallback)
        return worldPoint;
    }

    // Like ProjectOntoSurface but also returns the hit normal when found (otherwise Vector3.up)
    public static Vector3 ProjectOntoSurface(Vector3 worldPoint, out Vector3 outNormal, float upOffset = 2f, float maxDown = 25f, LayerMask? layerMask = null)
    {
        Vector3 origin = worldPoint + Vector3.up * upOffset;
        outNormal = Vector3.up;

        LayerMask mask = layerMask ?? (LayerMask.GetMask("RoadSurface"));
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, upOffset + maxDown, mask.value, QueryTriggerInteraction.Ignore))
        {
            outNormal = hit.normal;
            return new Vector3(worldPoint.x, hit.point.y, worldPoint.z);
        }
        if (Physics.Raycast(origin, Vector3.down, out hit, upOffset + maxDown, ~0, QueryTriggerInteraction.Ignore))
        {
            outNormal = hit.normal;
            return new Vector3(worldPoint.x, hit.point.y, worldPoint.z);
        }
        return worldPoint;
    }
}