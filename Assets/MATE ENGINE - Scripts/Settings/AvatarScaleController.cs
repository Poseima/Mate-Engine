using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Kirurobo; 
public class AvatarScaleController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Slider avatarSizeSlider;

    [Header("Scroll Settings")]
    [SerializeField] private float scrollSensitivity = 0.1f;
    [SerializeField] private float smoothFactor = 0.1f; 

    private float minSize;
    private float maxSize;
    private float targetSize;
    private Transform modelRoot;
    private GameObject currentModel;
    private AvatarAnimatorController controller;

    // When the pointer is over the Task Monitor UI, mouse wheel should scroll the monitor
    // without resizing the avatar.
    static Transform cachedTaskMonitorCanvas;
    static readonly List<RaycastResult> uiRaycastHits = new();

    void Start()
    {
        if (avatarSizeSlider == null) return;

        minSize = avatarSizeSlider.minValue;
        maxSize = avatarSizeSlider.maxValue;
        targetSize = avatarSizeSlider.value;

        var modelRootGO = GameObject.Find("Model");
        if (modelRootGO != null)
            modelRoot = modelRootGO.transform;
        avatarSizeSlider.onValueChanged.AddListener(v => targetSize = v);
    }

    public void SyncWithSlider()
    {
        if (avatarSizeSlider != null)
            targetSize = avatarSizeSlider.value;
    }

    void Update()
    {
        if (avatarSizeSlider == null)
            return;

        if (MenuActions.IsMovementBlocked())
            return;

        if (UniWindowController.current.isClickThrough)
            return;

        if (modelRoot != null)
        {
            GameObject activeModel = null;
            for (int i = 0; i < modelRoot.childCount; i++)
            {
                var child = modelRoot.GetChild(i);
                if (child.gameObject.activeInHierarchy)
                {
                    activeModel = child.gameObject;
                    break;
                }
            }

            if (activeModel != currentModel)
            {
                currentModel = activeModel;
                controller = currentModel != null ? currentModel.GetComponent<AvatarAnimatorController>() : null;
            }
        }

        if (controller != null && controller.isDragging)
            return;

        // Only apply scroll-to-scale when we're not hovering the Task Monitor.
        // We keep the rest of the Update() logic (smoothing, slider sync) intact.
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f && IsPointerOverTaskMonitor())
            scroll = 0f;

        if (scroll != 0f)
        {
            targetSize = Mathf.Clamp(
                targetSize + scroll * scrollSensitivity,
                minSize, maxSize
            );
        }

        float current = avatarSizeSlider.value;
        float smoothed = Mathf.Lerp(
            current,
            targetSize,
            1f - Mathf.Pow(1f - smoothFactor, Time.deltaTime * 60f)
        );

        if (Mathf.Abs(smoothed - current) > 0.0001f)
        {
            avatarSizeSlider.SetValueWithoutNotify(smoothed);
            avatarSizeSlider.value = smoothed;

            SaveLoadHandler.Instance.data.avatarSize = smoothed;
            SaveLoadHandler.Instance.SaveToDisk();
            SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        }
    }

    static bool IsPointerOverTaskMonitor()
    {
        if (EventSystem.current == null)
            return false;

        if (cachedTaskMonitorCanvas == null)
        {
            var canvasGo = GameObject.Find("TaskMonitorCanvas");
            if (canvasGo == null)
                return false;
            cachedTaskMonitorCanvas = canvasGo.transform;
        }

        // If the monitor canvas is disabled/hidden (e.g., BigScreen mode), don't block scroll-to-scale.
        var canvas = cachedTaskMonitorCanvas.GetComponent<Canvas>();
        if (canvas != null && !canvas.enabled)
            return false;

        var ped = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        uiRaycastHits.Clear();
        EventSystem.current.RaycastAll(ped, uiRaycastHits);

        for (int i = 0; i < uiRaycastHits.Count; i++)
        {
            var go = uiRaycastHits[i].gameObject;
            if (go != null && go.transform.IsChildOf(cachedTaskMonitorCanvas))
                return true;
        }

        return false;
    }
}
