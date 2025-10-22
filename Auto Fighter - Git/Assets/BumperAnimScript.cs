using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class BumperAnimScript : MonoBehaviour
{

    private Material defMaterial;
    private Color defMatColor;

    private Bumper bumper;

    [SerializeField] private bool resetHPBarAlphaToZero = true; // hide HP bar before each flash
    [SerializeField] private Image HPBar;              // assign your HP bar image here
    [SerializeField] private Color hpFlashColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float hpFlashAlpha = 0.9f;
    [SerializeField] private float hpFlashDuration = 0.18f; // total flash time
    [SerializeField] private Vector2 hpPunchScale = new Vector2(1.08f, 1.08f);
    [SerializeField] private int hpPunchVibrato = 9;        // how “wobbly” the punch is
    [SerializeField, Range(0f, 1f)] private float hpPunchElasticity = 0.12f;
    [SerializeField, Range(0f, .1f)] private float genScale = 0.04f; // general scale reduction to keep things in check

    private Vector3 _defLocalScale;        // default bumper scale
    private Vector3 _hpRTDefaultScale;     // default HP bar rect scale
    private float _hpGroupDefaultAlpha;    // default canvas group alpha

    private Color _hpDefaultColor;
    private RectTransform _hpRT;

    [SerializeField] private CanvasGroup hpGroup;


    void Awake()
    {
        if(hpGroup != null) hpGroup.alpha = 0f;
    }

    // Start is called before the first frame update
    void Start()
    {
        defMaterial = GetComponent<Renderer>().material;
        defMatColor = defMaterial.color;
        bumper = GetComponent<Bumper>();

        if(HPBar != null)
        {
            _hpDefaultColor = HPBar.color;
            _hpRT = HPBar.rectTransform;

        }

        if(hpGroup != null)
        {
            hpGroup.interactable = false;
            hpGroup.blocksRaycasts = false;
        }

        _defLocalScale = transform.localScale;

        if (_hpRT != null)
            _hpRTDefaultScale = _hpRT.localScale;

        if (hpGroup != null)
            _hpGroupDefaultAlpha = hpGroup.alpha;
    }

    // Update is called once per frame
    void Update()
    {
        HPBar.fillAmount = Mathf.MoveTowards(HPBar.fillAmount, bumper.curHealth / bumper.maxHealth, Time.deltaTime);
    }

    public void ResetTweenState()
    {
        transform.DOKill(false);
        if (defMaterial != null) defMaterial.DOKill(false);
        if (HPBar != null) HPBar.DOKill(false);
        if (_hpRT != null) _hpRT.DOKill(false);
        if (hpGroup != null) hpGroup.DOKill(false);

        transform.localScale = _defLocalScale;
        if(_hpRT != null) _hpRT.localScale = _hpRTDefaultScale;

        if (defMaterial != null) defMaterial.color = defMatColor;
        if(HPBar != null) HPBar.color = _hpDefaultColor;


        if(hpGroup != null)
        {
            hpGroup.alpha = resetHPBarAlphaToZero ? 0f : _hpGroupDefaultAlpha;
            hpGroup.interactable = false;
            hpGroup.blocksRaycasts = false;
        }

    }

    public void BumperHit()
    {
        ResetTweenState();
        DOTween.Kill(transform);
        transform.DOPunchScale(new Vector3(.3f, .3f, .3f), 0.2f, 2, .1f);
        defMaterial.DOColor(Color.white, 0.1f).OnComplete(() => {
            defMaterial.DOColor(defMatColor, 0.1f);
        });

        FlashHPBar();
    }

    private void FlashHPBar()
    {

        if (hpGroup != null)
        {
            DOTween.Kill(hpGroup);
            hpGroup.DOFade(1f, 0.05f).SetUpdate(true); // quick pop-in, pause-safe
        }

        if (HPBar == null) return;

        DOTween.Kill(HPBar);
        if (_hpRT != null) DOTween.Kill(_hpRT);

        var target = hpFlashColor;
        target.a = Mathf.Clamp01(hpFlashAlpha);

        var half = hpFlashDuration * 0.5f;

        // create seq first
        var seq = DOTween.Sequence().SetId(HPBar);

        // 1) flash up to target color
        seq.Append(HPBar.DOColor(target, half).SetEase(Ease.OutQuad));

        // 2) punch (guarded)
        if (_hpRT != null)
        {
            seq.Join(_hpRT.DOPunchScale(
                new Vector3(hpPunchScale.x - genScale, hpPunchScale.y - genScale, 0f),
                hpFlashDuration,
                hpPunchVibrato,
                hpPunchElasticity
            ).SetEase(Ease.OutQuad));
        }

        // 3) return to default color
        seq.Append(HPBar.DOColor(_hpDefaultColor, half).SetEase(Ease.InQuad));

        // 4) fade out the whole bar
        if (hpGroup != null)
            seq.Append(hpGroup.DOFade(0f, 0.25f).SetEase(Ease.InQuad).SetUpdate(true));

        // make the whole sequence run while paused
        seq.SetUpdate(true);

    }

}
