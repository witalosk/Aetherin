using TMPro;
using UnityEngine;

namespace Aetherin
{
    public static class TextSelectorUtility
    {
        public static float Evaluate(
            TextRangeSelectorParams selector,
            TMP_TextInfo textInfo,
            int characterIndex,
            string sourceText,
            in ModulationContext context)
        {
            if (selector == null || textInfo == null || characterIndex < 0 ||
                characterIndex >= textInfo.characterCount) return 0f;

            int itemIndex;
            int itemCount;
            TMP_CharacterInfo character = textInfo.characterInfo[characterIndex];

            switch (selector.BasedOn)
            {
                case TextSelectorBasedOn.CharactersExcludingSpaces:
                    itemIndex = 0;
                    itemCount = 0;
                    for (int i = 0; i < textInfo.characterCount; i++)
                    {
                        if (char.IsWhiteSpace(textInfo.characterInfo[i].character)) continue;
                        if (i < characterIndex) itemIndex++;
                        itemCount++;
                    }
                    if (char.IsWhiteSpace(character.character)) return 0f;
                    break;
                case TextSelectorBasedOn.Words:
                    GetWordIndex(sourceText, character.index, out itemIndex, out itemCount);
                    break;
                case TextSelectorBasedOn.Lines:
                    itemIndex = character.lineNumber;
                    itemCount = Mathf.Max(1, textInfo.lineCount);
                    break;
                default:
                    itemIndex = characterIndex;
                    itemCount = Mathf.Max(1, textInfo.characterCount);
                    break;
            }

            if (selector.RandomizeOrder && itemCount > 1)
                itemIndex = PositiveHash(itemIndex + selector.RandomSeed * 486187739) % itemCount;

            float percent = itemCount <= 1 ? 0f : itemIndex * 100f / (itemCount - 1f);
            float start = selector.Start?.Evaluate(context) ?? 0f;
            float end = selector.End?.Evaluate(context) ?? 100f;
            float offset = selector.Offset?.Evaluate(context) ?? 0f;
            float x = Mathf.Repeat(percent - offset, 100f);
            float low = Mathf.Min(start, end);
            float high = Mathf.Max(start, end);
            float width = Mathf.Max(0.0001f, high - low);
            float normalized = Mathf.Clamp01((x - low) / width);
            bool inside = x >= low && x <= high;

            float weight = selector.Shape switch
            {
                TextSelectorShape.RampUp => inside ? normalized : 0f,
                TextSelectorShape.RampDown => inside ? 1f - normalized : 0f,
                TextSelectorShape.Triangle => inside ? 1f - Mathf.Abs(normalized * 2f - 1f) : 0f,
                TextSelectorShape.Smooth => inside ? normalized * normalized * (3f - 2f * normalized) : 0f,
                _ => inside ? 1f : 0f,
            };

            float smoothness = Mathf.Clamp01((selector.Smoothness?.Evaluate(context) ?? 100f) / 100f);
            if (smoothness < 1f && weight > 0f)
                weight = Mathf.Lerp(1f, weight, smoothness);
            return start <= end ? weight : 1f - weight;
        }

        private static void GetWordIndex(string text, int stringIndex, out int wordIndex, out int wordCount)
        {
            wordIndex = 0;
            wordCount = 0;
            bool inWord = false;
            for (int i = 0; i < (text?.Length ?? 0); i++)
            {
                bool whitespace = char.IsWhiteSpace(text[i]);
                if (!whitespace && !inWord)
                {
                    if (i <= stringIndex) wordIndex = wordCount;
                    wordCount++;
                }
                inWord = !whitespace;
            }
            wordCount = Mathf.Max(1, wordCount);
        }

        private static int PositiveHash(int value)
        {
            unchecked
            {
                value = (value ^ 61) ^ (value >> 16);
                value += value << 3;
                value ^= value >> 4;
                value *= 0x27d4eb2d;
                value ^= value >> 15;
                return value & int.MaxValue;
            }
        }
    }
}
