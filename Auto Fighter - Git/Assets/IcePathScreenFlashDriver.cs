using UnityEngine;

[DisallowMultipleComponent]
public class IcePathScreenFlashDriver : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CarController car;
    [SerializeField] private ScreenFlashManager flash;

    private bool _lastIce;

    private void Awake()
    {
        if (!flash) flash = ScreenFlashManager.Instance;
    }

    private void Update()
    {
        if (!flash || !car) return;

        bool onIce = car.IsOnIceSurface;
        if (onIce == _lastIce) return;

        _lastIce = onIce;
        flash.SetIcePersistent(onIce);
    }


    public void SetCarController(CarController car)
    {
        this.car = car;
    }

}
