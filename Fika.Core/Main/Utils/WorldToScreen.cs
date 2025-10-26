using EFT.Animations;
using Fika.Core.Main.Players;

namespace Fika.Core.Main.Utils;

/// <summary>
/// Class to convert world positions to canvas positions, safe for DLSS / SSAA / viewport scaling
/// </summary>
public static class WorldToScreen
{

#if DEBUG
    private static WorldToScreenDebugger debugger;

    public static bool ToggleDebugger()
    {
        if (debugger != null)
        {
            GameObject.Destroy(debugger);
            debugger = null;
            return true;
        }

        debugger = CameraClass.Instance.Camera.gameObject.AddComponent<WorldToScreenDebugger>();
        return false;
    }
#endif

    /// <summary>
    /// Projects a world position to a canvas position
    /// </summary>
    public static bool ProjectToCanvas(Vector3 worldPosition, FikaPlayer mainPlayer, RectTransform canvasRect,
        out Vector2 canvasPosition, bool useOpticCamera = true, bool skipBehindCheck = false)
    {
        canvasPosition = Vector2.zero;

        var camClass = CameraClass.Instance;
        if (mainPlayer == null || camClass?.Camera == null)
        {
            return false;
        }

        var projCam = camClass.Camera;

        Vector2 canvasSize = canvasRect.sizeDelta;
        float scaleFactor = 1f;

        // Use optic camera if zoomed
        if (useOpticCamera && IsZoomedOpticAiming(mainPlayer.ProceduralWeaponAnimation))
        {
            var opticCam = camClass.OpticCameraManager.Camera;
            if (opticCam != null)
            {
                projCam = opticCam;
                canvasSize = opticCam.pixelRect.max;
                scaleFactor = canvasRect.sizeDelta.x / Screen.width;
            }
        }

        var viewportPoint = projCam.WorldToViewportPoint(worldPosition);

        // Check if point is in front of camera
        if (viewportPoint.z <= 0f && !skipBehindCheck)
        {
            return false;
        }

        // Convert normalized viewport (0-1) to canvas local position
        canvasPosition = new Vector2((viewportPoint.x - 0.5f) * canvasSize.x * scaleFactor,
            (viewportPoint.y - 0.5f) * canvasSize.y * scaleFactor);

#if DEBUG
        debugger?.UpdateData(viewportPoint,
        camClass.Camera.WorldToViewportPoint(worldPosition),
        canvasPosition,
        canvasSize,
        Screen.width,
        camClass.SSAA.GetCurrentSSRatio(),
        scaleFactor);
#endif

        return true;
    }

    private static bool IsZoomedOpticAiming(ProceduralWeaponAnimation weaponAnim)
    {
        if (weaponAnim == null)
        {
            return false;
        }

        return weaponAnim.IsAiming && weaponAnim.CurrentScope != null &&
            weaponAnim.CurrentScope.IsOptic && GetScopeZoomLevel(weaponAnim) > 1f;
    }

    private static float GetScopeZoomLevel(ProceduralWeaponAnimation weaponAnim)
    {
        var sight = weaponAnim?.CurrentAimingMod;
        if (sight == null)
        {
            return 1f;
        }

        return (sight.ScopeZoomValue != 0) ? sight.ScopeZoomValue : sight.GetCurrentOpticZoom();
    }

    /// <summary>
    /// Rotate an indicator if the target is off-screen
    /// </summary>
    public static void TargetOutOfSight(bool outOfSight, Vector3 indicatorPosition, RectTransform rectTransform, RectTransform canvasRect)
    {
        if (outOfSight)
        {
            rectTransform.rotation = Quaternion.Euler(RotationOutOfSightTargetIndicator(indicatorPosition, canvasRect));
        }
        else
        {
            rectTransform.rotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// Calculates off-screen indicator position (keeps it clamped at canvas edges)
    /// </summary>
    public static Vector3 OutOfRangeIndicatorPosition(Vector3 indicatorPosition, RectTransform canvasRect, float outOfSightOffset)
    {
        indicatorPosition.z = 0f;
        var canvasCenter = new Vector3(canvasRect.rect.width / 2f, canvasRect.rect.height / 2f, 0f) * canvasRect.localScale.x;
        indicatorPosition -= canvasCenter;

        var divX = ((canvasRect.rect.width / 2f) - outOfSightOffset) / Mathf.Abs(indicatorPosition.x);
        var divY = ((canvasRect.rect.height / 2f) - outOfSightOffset) / Mathf.Abs(indicatorPosition.y);

        if (divX < divY)
        {
            var angle = Vector3.SignedAngle(Vector3.right, indicatorPosition, Vector3.forward);
            indicatorPosition.x = Mathf.Sign(indicatorPosition.x) * (canvasRect.rect.width * 0.5f - outOfSightOffset) * canvasRect.localScale.x;
            indicatorPosition.y = Mathf.Tan(Mathf.Deg2Rad * angle) * indicatorPosition.x;
        }
        else
        {
            var angle = Vector3.SignedAngle(Vector3.up, indicatorPosition, Vector3.forward);
            indicatorPosition.y = Mathf.Sign(indicatorPosition.y) * (canvasRect.rect.height / 2f - outOfSightOffset) * canvasRect.localScale.y;
            indicatorPosition.x = -Mathf.Tan(Mathf.Deg2Rad * angle) * indicatorPosition.y;
        }

        indicatorPosition += canvasCenter;
        return indicatorPosition;
    }

    /// <summary>
    /// Calculates rotation for off-screen target indicator
    /// </summary>
    public static Vector3 RotationOutOfSightTargetIndicator(Vector3 indicatorPosition, RectTransform canvasRect)
    {
        var angle = Vector3.SignedAngle(Vector3.up,
            indicatorPosition - (new Vector3(canvasRect.rect.width / 2f, canvasRect.rect.height / 2f, 0f) * canvasRect.localScale.x),
            Vector3.forward);
        return new Vector3(0f, 0f, angle);
    }


#if DEBUG
    internal class WorldToScreenDebugger : MonoBehaviour
    {
        private Vector2 opticCameraPoint;
        private Vector2 worldCameraPoint;
        private Vector2 canvasPosition;
        private Vector2 canvasSize;

        private int screenWidth;
        private float ssaaRatio;
        private float scaleFactor;

        private Rect windowRect;

        protected void Awake()
        {
            opticCameraPoint = Vector2.zero;
            worldCameraPoint = Vector2.zero;
            canvasPosition = Vector2.zero;
            canvasSize = Vector2.zero;
            screenWidth = 0;
            ssaaRatio = 1f;
            scaleFactor = 1f;

            windowRect = new(20, 20, 300, 10);
        }

        public void UpdateData(Vector2 opticCamPoint, Vector2 worldCamPoint, Vector2 canvasPosition, Vector2 canvasSize, int screenWidth, float ssaaRatio, float scaleFactor)
        {
            this.opticCameraPoint = opticCamPoint;
            this.worldCameraPoint = worldCamPoint;
            this.canvasPosition = canvasPosition;
            this.canvasSize = canvasSize;
            this.screenWidth = screenWidth;
            this.ssaaRatio = ssaaRatio;
            this.scaleFactor = scaleFactor;
        }

        protected void OnGUI()
        {
            GUILayout.BeginArea(windowRect);
            GUILayout.BeginVertical();

            windowRect = GUILayout.Window(1, windowRect, DrawWindow, "WTS Debugger");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label($"OpticCamera.X: {this.opticCameraPoint.x:F4}");
            GUILayout.Label($"OpticCamera.Y: {this.opticCameraPoint.y:F4}");

            GUILayout.Label($"WorldCamera.X: {this.worldCameraPoint.x:F4}");
            GUILayout.Label($"WorldCamera.Y: {this.worldCameraPoint.y:F4}");

            GUILayout.Label($"Canvas Position: {this.canvasPosition.x:F4}, {this.canvasPosition.y:F4}");

            GUILayout.Label($"Canvas Size: {this.canvasSize.x:F4}, {this.canvasSize.y:F4}");
            GUILayout.Label($"Screen Width: {this.screenWidth}");
            GUILayout.Label($"SSAA Ratio: {this.ssaaRatio:F4}");
            GUILayout.Label($"Scale Factor: {this.scaleFactor:F4}");

            GUI.DragWindow();
        }
    }
#endif
}