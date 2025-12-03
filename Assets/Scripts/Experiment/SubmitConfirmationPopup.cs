using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Experiment
{
    /// <summary>
    /// Controls the confirmation popup that appears when trial is submitted.
    /// Works with World Space canvas content (Image + TextMeshProUGUI).
    /// Setup: Same structure as PathWarningPopup.
    /// </summary>
    public class SubmitConfirmationPopup : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TextMeshProUGUI _text;
        [SerializeField] private Image _icon;
        [SerializeField] private float _showDuration = 3f;
        [SerializeField] private float _fadeDuration = 0.5f;
        [SerializeField] private bool _lockToCamera = true;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private float _cameraDistance = 1.0f;

        private Coroutine _currentRoutine;

        private void Awake()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }

            if (_text == null)
            {
                _text = GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (_icon == null)
            {
                _icon = GetComponentInChildren<Image>(true);
            }

            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }

            SetAlpha(0f);
        }

        private void LateUpdate()
        {
            if (!_lockToCamera || _cameraTransform == null) return;
            if (_canvasGroup == null || _canvasGroup.alpha <= 0f) return;

            transform.position = _cameraTransform.position + _cameraTransform.forward * _cameraDistance;
            transform.rotation = Quaternion.LookRotation(transform.position - _cameraTransform.position, Vector3.up);
        }

        /// <summary>
        /// Shows the confirmation popup with the default "Submitted!" message.
        /// </summary>
        public void Show()
        {
            ShowMessage("Submitted!");
        }

        /// <summary>
        /// Displays the popup with the specified message.
        /// </summary>
        public void ShowMessage(string message)
        {
            if (_canvasGroup == null) return;

            if (_text != null)
            {
                _text.text = message;
            }

            if (_currentRoutine != null)
            {
                StopCoroutine(_currentRoutine);
            }

            gameObject.SetActive(true);
            _currentRoutine = StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            SetAlpha(1f);

            float elapsed = 0f;
            while (elapsed < _showDuration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            float fadeElapsed = 0f;
            while (fadeElapsed < _fadeDuration)
            {
                fadeElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(fadeElapsed / _fadeDuration);
                SetAlpha(Mathf.Lerp(1f, 0f, t));
                yield return null;
            }

            SetAlpha(0f);
            gameObject.SetActive(false);
            _currentRoutine = null;
        }

        private void SetAlpha(float alpha)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = Mathf.Clamp01(alpha);
        }
    }
}

