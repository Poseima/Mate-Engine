using UnityEngine;
using UnityEngine.UI;
using System;
using LLMUnity;

namespace LLMUnitySamples
{
    struct BubbleUI
    {
        public Sprite sprite;
        public Font font;
        public int fontSize;
        public Color fontColor;
        public Color bubbleColor;
        public float bottomPosition;
        public float leftPosition;
        public float textPadding;
        public float bubbleOffset;
        public float bubbleWidth;
        public float bubbleHeight;
    }

    public class RectTransformResizeHandler : MonoBehaviour
    {
        EmptyCallback callback;

        public void SetCallBack(EmptyCallback callback)
        {
            this.callback = callback;
        }

        void OnRectTransformDimensionsChange()
        {
            callback?.Invoke();
        }
    }

    class Bubble
    {
        protected GameObject bubbleObject;
        protected GameObject imageObject;
        protected GameObject textObject;
        public BubbleUI bubbleUI;

        public Bubble(Transform parent, BubbleUI ui, string name, string message)
        {
            bubbleUI = ui;
            bool horizontalStretch = bubbleUI.bubbleWidth == -1;
            bool verticalStretch = bubbleUI.bubbleHeight == -1;

            // bubbleObject: invisible container = text area (no Graphic, no Canvas)
            // RectMask2D on the scroll viewport clips children since no child has its own Canvas.
            bubbleObject = new GameObject(name, typeof(RectTransform));
            bubbleObject.transform.SetParent(parent);

            // VLG relays child preferred size to CSF (replaces original CSF-on-Text)
            VerticalLayoutGroup vlg = bubbleObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            if (verticalStretch || horizontalStretch)
            {
                ContentSizeFitter csf = bubbleObject.AddComponent<ContentSizeFitter>();
                if (verticalStretch) csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                if (horizontalStretch) csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            // imageObject: first child → renders behind text (sibling order)
            imageObject = new GameObject("Image", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(bubbleObject.transform);
            Image bubbleImage = imageObject.GetComponent<Image>();
            bubbleImage.type = Image.Type.Sliced;
            bubbleImage.sprite = bubbleUI.sprite;
            bubbleImage.color = bubbleUI.bubbleColor;
            LayoutElement imgLE = imageObject.AddComponent<LayoutElement>();
            imgLE.ignoreLayout = true;

            // textObject: second child → renders on top of image
            textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(bubbleObject.transform);
            Text textContent = textObject.GetComponent<Text>();
            textContent.text = message;
            if (bubbleUI.font != null)
                textContent.font = bubbleUI.font;
            textContent.fontSize = bubbleUI.fontSize;
            textContent.color = bubbleUI.fontColor;

            SetBubblePosition(bubbleObject.GetComponent<RectTransform>(), imageObject.GetComponent<RectTransform>(), bubbleUI);
        }

        public void SyncParentRectTransform(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        void SetBubblePosition(RectTransform bubbleRectTransform, RectTransform imageRectTransform, BubbleUI bubbleUI)
        {
            // Set the position of the new bubble at the bottom
            bubbleRectTransform.pivot = new Vector2(bubbleUI.leftPosition, bubbleUI.bottomPosition);
            bubbleRectTransform.anchorMin = new Vector2(bubbleUI.leftPosition, bubbleUI.bottomPosition);
            bubbleRectTransform.anchorMax = new Vector2(bubbleUI.leftPosition, bubbleUI.bottomPosition);
            bubbleRectTransform.localScale = Vector3.one;
            Vector2 anchoredPosition = new Vector2(bubbleUI.bubbleOffset + bubbleUI.textPadding, bubbleUI.bubbleOffset + bubbleUI.textPadding);
            if (bubbleUI.leftPosition == 1) anchoredPosition.x *= -1;
            if (bubbleUI.bottomPosition == 1) anchoredPosition.y *= -1;
            bubbleRectTransform.anchoredPosition = anchoredPosition;

            float width = bubbleUI.bubbleWidth == -1 ? bubbleRectTransform.sizeDelta.x : bubbleUI.bubbleWidth;
            float height = bubbleUI.bubbleHeight == -1 ? bubbleRectTransform.sizeDelta.y : bubbleUI.bubbleHeight;
            bubbleRectTransform.sizeDelta = new Vector2(width - 2 * bubbleUI.textPadding, height - 2 * bubbleUI.textPadding);
            SyncParentRectTransform(imageRectTransform);
            imageRectTransform.offsetMin = new Vector2(-bubbleUI.textPadding, -bubbleUI.textPadding);
            imageRectTransform.offsetMax = new Vector2(bubbleUI.textPadding, bubbleUI.textPadding);
        }

        public void OnResize(EmptyCallback callback)
        {
            RectTransformResizeHandler resizeHandler = bubbleObject.AddComponent<RectTransformResizeHandler>();
            resizeHandler.SetCallBack(callback);
        }

        public RectTransform GetRectTransform()
        {
            return bubbleObject.GetComponent<RectTransform>();
        }

        public RectTransform GetOuterRectTransform()
        {
            return imageObject.GetComponent<RectTransform>();
        }

        public Vector2 GetSize()
        {
            return bubbleObject.GetComponent<RectTransform>().sizeDelta
                 + new Vector2(2 * bubbleUI.textPadding, 2 * bubbleUI.textPadding);
        }

        public string GetText()
        {
            return textObject.GetComponent<Text>().text;
        }

        public void SetText(string text)
        {
            textObject.GetComponent<Text>().text = text;
        }

        public void Destroy()
        {
            UnityEngine.Object.Destroy(bubbleObject);
        }
    }

    class InputBubble : Bubble
    {
        protected GameObject inputFieldObject;
        protected InputField inputField;
        protected GameObject placeholderObject;

        public InputBubble(Transform parent, BubbleUI ui, string name, string message, int lineHeight = 4) :
            base(parent, ui, name, emptyLines(message, lineHeight))
        {
            Text textObjext = textObject.GetComponent<Text>();
            RectTransform bubbleRectTransform = bubbleObject.GetComponent<RectTransform>();

            // Disable auto-sizing for input bubble — size is fixed
            ContentSizeFitter csf = bubbleObject.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = false;
            VerticalLayoutGroup vlg = bubbleObject.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = false;

            // textObject: stretch to fill parent (bubbleObject IS the text area)
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            SyncParentRectTransform(textRect);

            placeholderObject = CreatePlaceholderObject(bubbleObject.transform, textObjext.text);
            inputFieldObject = CreateInputFieldObject(bubbleObject.transform, textObjext, placeholderObject.GetComponent<Text>());
            inputField = inputFieldObject.GetComponent<InputField>();
        }


        static string emptyLines(string message, int lineHeight)
        {
            string messageLines = message;
            for (int i = 0; i < lineHeight - 1; i++)
                messageLines += "\n";
            return messageLines;
        }

        GameObject CreatePlaceholderObject(Transform parent, string message)
        {
            GameObject placeholderObject = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            placeholderObject.transform.SetParent(parent);
            Text textContent = placeholderObject.GetComponent<Text>();
            textContent.text = message;
            if (bubbleUI.font != null)
                textContent.font = bubbleUI.font;
            textContent.fontSize = bubbleUI.fontSize;
            textContent.color = bubbleUI.fontColor;
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.localScale = Vector3.one;
            SyncParentRectTransform(placeholderRect);
            return placeholderObject;
        }

        GameObject CreateInputFieldObject(Transform parent, Text textObject, Text placeholderTextObject)
        {
            GameObject inputFieldObject = new GameObject("InputField", typeof(RectTransform), typeof(InputField));
            inputFieldObject.transform.SetParent(parent);
            inputField = inputFieldObject.GetComponent<InputField>();
            inputField.textComponent = textObject;
            inputField.placeholder = placeholderTextObject;
            inputField.interactable = true;
            inputField.lineType = InputField.LineType.MultiLineSubmit;
            inputField.shouldHideMobileInput = false;
            inputField.shouldActivateOnSelect = true;
            RectTransform inputFieldRect = inputFieldObject.GetComponent<RectTransform>();
            inputFieldRect.localScale = Vector3.one;
            SyncParentRectTransform(inputFieldRect);
            return inputFieldObject;
        }

        public void FixCaretSorting()
        {
            GameObject caret = GameObject.Find($"{inputField.name} Input Caret");
            if (caret == null) return;
            Canvas bubbleCanvas = caret.GetComponent<Canvas>();
            if (bubbleCanvas == null)
            {
                bubbleCanvas = caret.AddComponent<Canvas>();
                bubbleCanvas.overrideSorting = true;
                bubbleCanvas.sortingOrder = 3;
            }
        }

        public void AddSubmitListener(UnityEngine.Events.UnityAction<string> onInputFieldSubmit)
        {
            inputField.onSubmit.AddListener(onInputFieldSubmit);
        }

        public void AddValueChangedListener(UnityEngine.Events.UnityAction<string> onValueChanged)
        {
            inputField.onValueChanged.AddListener(onValueChanged);
        }

        public new string GetText()
        {
            return inputField.text;
        }

        public new void SetText(string text)
        {
            inputField.text = text;
            MoveTextEnd();
        }

        public void SetPlaceHolderText(string text)
        {
            placeholderObject.GetComponent<Text>().text = text;
        }

        public bool inputFocused()
        {
            return inputField.isFocused;
        }

        public void MoveTextEnd()
        {
            inputField.MoveTextEnd(true);
        }

        public void setInteractable(bool interactable)
        {
            inputField.interactable = interactable;
        }

        public void SetSelectionColorAlpha(float alpha)
        {
            Color color = inputField.selectionColor;
            color.a = alpha;
            inputField.selectionColor = color;
        }

        public void ActivateInputField()
        {
            inputField.ActivateInputField();
            FixCaretSorting();
        }

        public void ReActivateInputField()
        {
            inputField.DeactivateInputField();
            inputField.Select();
            inputField.ActivateInputField();
        }
    }

}
