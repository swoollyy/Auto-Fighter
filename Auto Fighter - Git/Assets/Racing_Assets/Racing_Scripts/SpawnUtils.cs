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

    /// <summary>
    /// Cast straight down from well above the heightmap so the ray cannot start inside
    /// a hill or road collider. Returns the highest hit on <paramref name="mask"/>.
    /// </summary>
    public static bool TryRaycastDownFromHigh(
        Vector3 xzWorld, LayerMask mask, float clearanceAbove, float downPastSurface, out RaycastHit hit)
    {
        hit = default;
        float wx = xzWorld.x;
        float wz = xzWorld.z;
        float surfaceY = xzWorld.y;

        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains != null)
        {
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain t = terrains[i];
                if (t == null || t.terrainData == null) continue;
                Vector3 tp = t.transform.position;
                Vector3 sz = t.terrainData.size;
                if (wx < tp.x || wx > tp.x + sz.x || wz < tp.z || wz > tp.z + sz.z)
                    continue;
                float y = t.SampleHeight(new Vector3(wx, 0f, wz)) + tp.y;
                if (y > surfaceY) surfaceY = y;
            }
        }

        float originY = surfaceY + Mathf.Max(8f, clearanceAbove);
        Vector3 origin = new Vector3(wx, originY, wz);
        float maxDist = Mathf.Max(20f, clearanceAbove + downPastSurface + 40f);

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDist, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        int best = 0;
        for (int i = 1; i < hits.Length; i++)
        {
            if (hits[i].point.y > hits[best].point.y)
                best = i;
        }

        hit = hits[best];
        return true;
    }

    /// <summary>
    /// Random world Y on the spawned root only. Child meshes keep their authored local pose.
    /// Child rigidbodies are made kinematic first so they cannot snap the root back to identity.
    /// </summary>
    public static void RandomizeWorldYaw(GameObject go)
    {
        if (go == null) return;

        float yaw = Random.Range(0f, 360f);
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

        Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            Rigidbody rb = rbs[i];
            if (rb == null) continue;
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        go.transform.rotation = yawRot;

        SyncRigidbodies(go);
    }

    public static void ForceKinematicNoGravity(GameObject go)
    {
        if (go == null) return;

        Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            Rigidbody rb = rbs[i];
            if (rb == null) continue;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        SyncRigidbodies(go);
    }

    public static bool IsEmbeddedLocked(Component c)
    {
        return TerrainEmbeddedAnchor.IsLocked(c);
    }

    public static bool IsEmbeddedLocked(GameObject go)
    {
        return TerrainEmbeddedAnchor.IsLocked(go);
    }

    /// <summary>
    /// Freeze a parent that was spawned inside terrain. Pose stays put; hits cannot unstick it.
    /// </summary>
    public static void LockEmbeddedInTerrain(GameObject go)
    {
        if (go == null) return;
        ForceKinematicNoGravity(go);
        TerrainEmbeddedAnchor.Attach(go);
    }

    public static bool CanSafelySimulateAgainstTerrain(GameObject go)
    {
        if (go == null) return false;

        bool anySolid = false;
        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            anySolid = true;
            if (col is MeshCollider meshCol && !meshCol.convex)
                return false;
        }

        return anySolid;
    }

    public static void SyncRigidbodies(GameObject go)
    {
        if (go == null) return;

        Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            Rigidbody rb = rbs[i];
            if (rb == null) continue;
            rb.position = rb.transform.position;
            rb.rotation = rb.transform.rotation;
        }
    }

    public static bool IsInCameraFrustum(GameObject go, Plane[] frustumPlanes)
    {
        if (go == null || frustumPlanes == null || frustumPlanes.Length == 0)
            return false;

        Renderer[] rends = go.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
        {
            Renderer r = rends[i];
            if (r == null || !r.enabled)
                continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                continue;
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, r.bounds))
                return true;
        }

        return false;
    }
}
