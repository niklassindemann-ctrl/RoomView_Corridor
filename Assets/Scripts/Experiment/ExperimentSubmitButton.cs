using UnityEngine;
using UnityEngine.UI;

namespace Experiment
{
    /// <summary>
    /// Component for the Submit button that triggers trial data saving.
    /// Attach this to your wrist menu's Submit button GameObject.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ExperimentSubmitButton : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private SubmitConfirmationPopup _confirmationPopup;

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
            if (_button != null)
            {
                _button.onClick.AddListener(OnSubmitClicked);
            }
        }

        private void OnSubmitClicked()
        {
            var experimentManager = ExperimentDataManager.Instance;
            if (experimentManager == null)
            {
                Debug.LogError("[ExperimentSubmitButton] ExperimentDataManager not found in scene!");
                return;
            }

            // Submit trial data
            experimentManager.SubmitTrial();

            // Show confirmation popup
            if (_confirmationPopup != null)
            {
                _confirmationPopup.Show();
            }

            Debug.Log("[ExperimentSubmitButton] Trial submitted successfully!");
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnSubmitClicked);
            }
        }
    }
}
