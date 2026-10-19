using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.Localization
{
    /// <summary>
    /// 고정 글자 전용(인자 없음): 켜질 때와 언어가 바뀔 때 key의 글자로 Text를 다시 쓴다.
    /// 코드가 매번 다시 쓰는 글자(상태 메시지·수치)는 이 컴포넌트 대신 그 자리에서 Loc.Get을 부른다.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string key;

        private Text _text;

        public string Key => key;

        /// <summary>키를 바꾸고 바로 글자를 갱신한다(코드로 붙일 때).</summary>
        public void SetKey(string newKey)
        {
            key = newKey;
            Refresh();
        }

        private void OnEnable()
        {
            Refresh();
            Loc.Changed += Refresh;
        }

        private void OnDisable()
        {
            Loc.Changed -= Refresh;
        }

        private void Refresh()
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (_text == null)
            {
                _text = GetComponent<Text>();
            }

            _text.text = Loc.Get(key);
        }
    }
}
