# Localization · 현지화

English, 한국어 and 中文 for every player-facing string.

- **Translations:** `Data/strings.csv` (columns `zh, en, ko, context`). Edit `en` / `ko` only; never change the `zh` key.
- **In code:** `Loc.T("中文原文")`, or `Loc.F("进度  {0}/{1}", a, b)` for text with numbers.
- **In game:** press **L** to cycle the language. **In the editor:** *Tools › Distorted Space › Language*.
- **Missing rows:** *Tools › Distorted Space › Localization › Report Untranslated Scene Text*.

Full guide: `docs/en/localization.md` · 한국어 안내: `docs/ko/localization.md`
