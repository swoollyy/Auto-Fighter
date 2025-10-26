using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PowerupPickupTween : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Visual root to animate. Defaults to self if null.")]
    public Transform model;

    [Header("Update")]
    [Tooltip("DOTween update type.")]
    public UpdateType updateType = UpdateType.Normal;
    [Tooltip("Ignore Time.timeScale (use unscaled time).")]
    public bool independentUpdate = true;

    [Header("Spawn")]
    public float spawnScaleFrom = 0.0f;
    public float spawnScaleTo = 1.0f;
    public float spawnDuration = 0.35f;
    public Ease spawnEase = Ease.OutBack;

    [Header("Idle Hover")]
    public float hoverAmplitude = 0.15f;
    public float hoverHalfCycle = 0.6f;
    public Ease hoverEase = Ease.InOutSine;

    [Header("Rotate")]
    public float rotateSpeedDegPerSec = 90f;

    [Header("Collect Punch")]
    public float collectPunchScale = 0.25f;
    public float collectDuration = 0.18f;
    public Ease collectEase = Ease.OutBack;

    private Vector3 _baseLocalPos;
    private Tweener _hoverTw;
    private Tweener _rotateTw;
    private Tweener _spawnTw;

    void Awake()
    {
        if (!model) model = transform;
        _baseLocalPos = model.localPosition;
    }

    void OnDisable()
    {
        KillAllTweens();
        if (model)
        {
            model.localPosition = _baseLocalPos;
            model.localScale = Vector3.one;
        }
    }

    public void PlaySpawn()
    {
        if (!model) return;
        KillAllTweens();

        _baseLocalPos = model.localPosition;
        model.localScale = Vector3.one * Mathf.Max(0f, spawnScaleFrom);

        _spawnTw = model
            .DOScale(spawnScaleTo, Mathf.Max(0.01f, spawnDuration))
            .SetEase(spawnEase)
            .SetUpdate(updateType, independentUpdate)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
            .OnComplete(StartIdle);

        float lift = Mathf.Abs(hoverAmplitude) * 0.5f;
        model.DOLocalMoveY(_baseLocalPos.y + lift, spawnDuration * 0.5f)
             .SetLoops(2, LoopType.Yoyo)
             .SetEase(Ease.OutSine)
             .SetUpdate(updateType, independentUpdate)
             .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    public void PlayCollect()
    {
        if (!model) return;

        model.DOPunchScale(Vector3.one * collectPunchScale,
                           Mathf.Max(0.05f, collectDuration), vibrato: 1, elasticity: 0.5f)
             .SetEase(collectEase)
             .SetUpdate(updateType, independentUpdate)
             .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    public float GetCollectDuration() => Mathf.Max(0.05f, collectDuration);

    private void StartIdle()
    {
        if (!model) return;

        if (Mathf.Abs(hoverAmplitude) > 0.0001f)
        {
            _hoverTw = model.DOLocalMoveY(_baseLocalPos.y + hoverAmplitude,
                                          Mathf.Max(0.05f, hoverHalfCycle))
                           .SetEase(hoverEase)
                           .SetLoops(-1, LoopType.Yoyo)
                           .SetUpdate(updateType, independentUpdate)
                           .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        if (Mathf.Abs(rotateSpeedDegPerSec) > 0.01f)
        {
            float oneTurnTime = 360f / Mathf.Abs(rotateSpeedDegPerSec);
            _rotateTw = model.DOLocalRotate(
                            new Vector3(0f, Mathf.Sign(rotateSpeedDegPerSec) * 360f, 0f),
                            oneTurnTime, RotateMode.LocalAxisAdd)
                        .SetEase(Ease.Linear)
                        .SetLoops(-1, LoopType.Incremental)
                        .SetUpdate(updateType, independentUpdate)
                        .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }
    }

    private void KillAllTweens()
    {
        _spawnTw?.Kill();
        _hoverTw?.Kill();
        _rotateTw?.Kill();
        _spawnTw = _hoverTw = _rotateTw = null;
    }
}