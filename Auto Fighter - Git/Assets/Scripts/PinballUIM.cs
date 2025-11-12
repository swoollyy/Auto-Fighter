using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PinballUIM : MonoBehaviour
{
    [System.Serializable]
    public class RewardSlot
    {
        public Button button;
        public TMP_Text titleText;
        public TMP_Text descText;
    }

    [Header("Per-Ball Combo Row")]
    [SerializeField] private Transform ballRowParent;
    [SerializeField] private GameObject ballEntryPrefab;

    private readonly Dictionary<Ball, BallUIEntry> _ballEntries = new();

    [Header("UI Slots(6 buttons)")]
    [SerializeField] private List<RewardSlot> slots = new();

    [Header("Lives UI")]
    [SerializeField] private List<Image> lifeIcons = new();
    [SerializeField] private Color32 lifeOnColor = new Color32(75, 202, 107, 255);
    [SerializeField] private Color32 lifeOffColor = new Color32(75, 202, 107, 11);

    [Header("Ability: Portal Cooldown")]
    [SerializeField] private Transform portalCooldownGroup;
    [SerializeField] private Image portalCooldownTemplate;
    [SerializeField] private Color32 portalReadyFallback = new Color32(170, 85, 255, 255);
    [SerializeField] private Color32 portalCooldownColor = new Color32(255, 255, 255, 255); // retained in case other UI needs it

    [Header("Ability: Grenade Cooldown")]
    [SerializeField] private Transform grenadeCooldownGroup;
    [SerializeField] private Image grenadeCooldownTemplate;
    private readonly Dictionary<Ball, Image> _grenadeIconByBall = new();

    private readonly Dictionary<Ball, Image> _portalIconByBall = new();

    public Image ChargingSlider;

    public GameObject gamePanel;
    public GameObject paddleSelectPanel;
    public GameObject levelUpPanel;

    public TMP_Text gameScore;
    public TMP_Text bc;
    public TMP_Text bcc;
    public TMP_Text xpText;

    private Pinball pm;
    private List<RewardSO> currentRewards = new();

    void Start()
    {
        levelUpPanel.SetActive(false);
        paddleSelectPanel.SetActive(false);
        gamePanel.SetActive(true);

        // Fallbacks: if grenade row not set, reuse portal row/template
        if (!grenadeCooldownTemplate && portalCooldownTemplate)
            grenadeCooldownTemplate = portalCooldownTemplate;
        if (!grenadeCooldownGroup && portalCooldownGroup)
            grenadeCooldownGroup = portalCooldownGroup;
    }

    void Update()
    {
        if (pm != null)
        {
            ChargingSlider.fillAmount = pm.chargePercentage;
            bc.text = $"Score Mult: {pm.ScoreMultiplier:F2} | Timer: {pm.ScoreBonusTimeRemaining:F2}";
            bcc.text = $"XP Mult: {pm.XPMultiplier:F2} | Timer: {pm.XPBonusTimeRemaining:F2}";
            if (Mathf.RoundToInt(pm.curXP) >= Mathf.RoundToInt(pm.maxXP))
                xpText.text = $"{Mathf.RoundToInt(pm.curXP - 1)} / {pm.maxXP}";
            else
                xpText.text = $"{Mathf.RoundToInt(pm.curXP)} / {pm.maxXP}";
        }
    }

    public void Init(Pinball manager)
    {
        pm = manager;
        Ball.OnBallActivated -= HandleBallActivated;
        Ball.OnBallActivated += HandleBallActivated;
        Ball.OnBallDeactivated -= HandleBallDeactivated;
        Ball.OnBallDeactivated += HandleBallDeactivated;

        var existing = GameObject.FindObjectsOfType<Ball>();
        for (int i = 0; i < existing.Length; i++)
            if (existing[i].isActiveAndEnabled && existing[i].IsActive)
                HandleBallActivated(existing[i]);
    }

    public void InitLives(int maxLives)
    {
        for (int i = 0; i < lifeIcons.Count; i++)
            lifeIcons[i].gameObject.SetActive(i < maxLives);
    }

    public void UpdateLives(int lives, int maxLives)
    {
        InitLives(maxLives);
        for (int i = 0; i < lifeIcons.Count && i < maxLives; i++)
            lifeIcons[i].color = i < lives ? lifeOnColor : lifeOffColor;
    }

    public void ShowRewardPopup(List<RewardSO> rewards)
    {
        gamePanel.SetActive(false);
        levelUpPanel.SetActive(true);
        currentRewards = rewards ?? new List<RewardSO>();

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.button.onClick.RemoveAllListeners();

            if (i < currentRewards.Count && currentRewards[i] != null)
            {
                var reward = currentRewards[i];
                slot.titleText.text = reward.Name;
                slot.titleText.color = RewardSO.GetRarityColor(reward.Rarity);
                slot.descText.text = reward.Description;
                slot.button.interactable = true;
                slot.button.gameObject.SetActive(true);
                slot.button.onClick.AddListener(() => OnRewardClicked(reward));
            }
            else
            {
                slot.titleText.text = string.Empty;
                slot.titleText.color = Color.white;
                slot.descText.text = string.Empty;
                slot.button.gameObject.SetActive(false);
            }
        }
    }

    private void OnRewardClicked(RewardSO reward)
    {
        pm.OnRewardChosen(reward);
    }

    public void DefaultUI()
    {
        levelUpPanel.SetActive(false);
        paddleSelectPanel.SetActive(false);
        gamePanel.SetActive(true);
    }

    public void UpdateScore(int score, int bumpCount, int bumpCountConsec)
    {
        gameScore.text = score.ToString();
    }

    public void PaddleSelect()
    {
        gamePanel.SetActive(false);

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.button.interactable = false;
        }
        paddleSelectPanel.SetActive(true);
    }

    public void ClosePaddleSelect(bool hasMoreLevels)
    {
        paddleSelectPanel.SetActive(false);
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.button.interactable = true;
        }

        if (hasMoreLevels)
            levelUpPanel.SetActive(true);
    }

    // ================= PORTAL WARP UI (TEMPLATE-BASED) =================
    // These methods now operate on the main ball's per-ball icon instead of a single Image field.
    public void SetPortalWarpReady(bool ready)
    {
        var mainBall = Pinball.Instance ? Pinball.Instance.ball : null;
        if (!mainBall) return;

        EnsurePortalIcon(mainBall);
        var img = _portalIconByBall[mainBall];
        img.enabled = true;
        img.color = Boost(mainBall.GlowColor, mainBall.EmissionIntensityUI);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = 2;
        img.fillClockwise = true;
        img.fillAmount = 1f; // filled when ready
    }

    public void SetPortalWarpCooldown(float normalizedRemaining)
    {
        var mainBall = Pinball.Instance ? Pinball.Instance.ball : null;
        if (!mainBall) return;

        EnsurePortalIcon(mainBall);
        var img = _portalIconByBall[mainBall];
        img.enabled = true;
        img.color = Boost(mainBall.GlowColor, mainBall.EmissionIntensityUI);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = 2;
        img.fillClockwise = true;
        img.fillAmount = Mathf.Clamp01(normalizedRemaining);
    }

    public void RegisterPortalIcon(Ball b)
    {
        if (!b || _portalIconByBall.ContainsKey(b)) return;
        if (!portalCooldownTemplate || !portalCooldownGroup) return;

        var clone = Instantiate(portalCooldownTemplate, portalCooldownGroup);
        clone.gameObject.name = $"PortalCooldown_{b.name}";
        clone.enabled = true;
        clone.color = Boost(b.GlowColor, b.EmissionIntensityUI); // emission color from start
        clone.type = Image.Type.Filled;
        clone.fillMethod = Image.FillMethod.Radial360;
        clone.fillOrigin = 2;
        clone.fillClockwise = true;
        clone.fillAmount = 1f; // start as filled (ready)
        _portalIconByBall[b] = clone;
    }

    public void UnregisterPortalIcon(Ball b)
    {
        if (!b) return;
        if (_portalIconByBall.TryGetValue(b, out var img))
        {
            if (img) Destroy(img.gameObject);
        }
        _portalIconByBall.Remove(b);
    }

    public void SetBallPortalReady(Ball b, bool ready)
    {
        EnsurePortalIcon(b);
        var img = _portalIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = 1f; // filled when ready
    }

    public void SetBallPortalCooldown(Ball b, float normalizedRemaining)
    {
        EnsurePortalIcon(b);
        var img = _portalIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = Mathf.Clamp01(normalizedRemaining);
    }

    private void EnsurePortalIcon(Ball b)
    {
        if (!b) return;
        if (!_portalIconByBall.ContainsKey(b))
            RegisterPortalIcon(b);
    }
    // ================================================================

    private void HandleBallActivated(Ball b)
    {
        if (!ballRowParent || !b || _ballEntries.ContainsKey(b)) return;

        var go = ballEntryPrefab
            ? Instantiate(ballEntryPrefab, ballRowParent)
            : CreateFallbackEntry(ballRowParent);

        var entry = go.GetComponent<BallUIEntry>();
        entry.Init(b);
        _ballEntries[b] = entry;

        b.OnComboChanged -= OnBallComboChanged;
        b.OnComboChanged += OnBallComboChanged;

        entry.Refresh(b);
    }

    private void HandleBallDeactivated(Ball b)
    {
        if (!b) return;

        b.OnComboChanged -= OnBallComboChanged;

        if (_ballEntries.TryGetValue(b, out var entry))
        {
            if (entry) Destroy(entry.gameObject);
            _ballEntries.Remove(b);
        }

        UnregisterPortalIcon(b);
        UnregisterGrenadeIcon(b); // also clean grenade icon
    }

    private void OnBallComboChanged(Ball b)
    {
        if (b != null && _ballEntries.TryGetValue(b, out var entry) && entry != null)
            entry.Refresh(b);
    }

    private GameObject CreateFallbackEntry(Transform parent)
    {
        var root = new GameObject("BallEntry", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var imgGO = new GameObject("Dot", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        imgGO.transform.SetParent(root.transform, false);

        var txtGO = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        txtGO.transform.SetParent(root.transform, false);

        var entry = root.AddComponent<BallUIEntry>();
        entry.BindRuntime(imgGO.GetComponent<UnityEngine.UI.Image>(), txtGO.GetComponent<TMPro.TextMeshProUGUI>());
        return root;
    }

    // ===== Grenade row =====
    public void RegisterGrenadeIcon(Ball b)
    {
        if (!b || _grenadeIconByBall.ContainsKey(b)) return;
        if (!grenadeCooldownTemplate || !grenadeCooldownGroup) return;

        var clone = Instantiate(grenadeCooldownTemplate, grenadeCooldownGroup);
        clone.gameObject.name = $"GrenadeCooldown_{b.name}";
        clone.enabled = true;
        clone.type = Image.Type.Filled;
        clone.fillMethod = Image.FillMethod.Radial360;
        clone.fillOrigin = 2;
        clone.fillClockwise = true;
        clone.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        clone.fillAmount = 0f;
        _grenadeIconByBall[b] = clone;
    }

    public void UnregisterGrenadeIcon(Ball b)
    {
        if (!b) return;
        if (_grenadeIconByBall.TryGetValue(b, out var img))
        {
            if (img) Destroy(img.gameObject);
        }
        _grenadeIconByBall.Remove(b);
    }

    public void SetBallGrenadeReady(Ball b, bool ready)
    {
        EnsureGrenadeIcon(b);
        var img = _grenadeIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = ready ? 1f : 0f; // filled when ready
    }

    public void SetBallGrenadeCooldown(Ball b, float normalizedRemaining)
    {
        EnsureGrenadeIcon(b);
        var img = _grenadeIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = Mathf.Clamp01(normalizedRemaining);
    }

    private void EnsureGrenadeIcon(Ball b)
    {
        if (!b) return;
        if (!_grenadeIconByBall.ContainsKey(b))
            RegisterGrenadeIcon(b);
    }

    private static Color Boost(Color c, float intensity)
    {
        float k = Mathf.Clamp(intensity, 0.5f, 2.5f);
        return new Color(
            Mathf.Clamp01(c.r * k),
            Mathf.Clamp01(c.g * k),
            Mathf.Clamp01(c.b * k),
            1f);
    }

    void OnDestroy()
    {
        Ball.OnBallActivated -= HandleBallActivated;
        Ball.OnBallDeactivated -= HandleBallDeactivated;

        foreach (var kv in _ballEntries)
            if (kv.Key != null)
                kv.Key.OnComboChanged -= OnBallComboChanged;

        foreach (var kv in _portalIconByBall)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _portalIconByBall.Clear();

        foreach (var kv in _grenadeIconByBall)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _grenadeIconByBall.Clear();
    }
}