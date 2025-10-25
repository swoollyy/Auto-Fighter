using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPowerup
{
    string Id { get; }
    float Weight { get; }
    string DebugLabel { get; }
    bool CanTrigger(IRunContext ctx);
    void Execute(Pinball pm, Vector3 triggerPos);

}
