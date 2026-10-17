# Translating Jemur

Every piece of text in Jemur lives in a JSON file in [`/locales`](../locales). English (`en.json`) is the source; other files mirror its keys.

## Add a language

1. Copy `locales/en.json` to `locales/<code>.json`, where `<code>` is the language code Windows uses — for example `de`, `pt-BR`, `ja`.
2. Set `_name` to the language's own name (`Deutsch`) and `_englishName` to its English name (`German`).
3. Translate the values. Leave the keys alone.
4. Keep placeholders like `{0}` exactly as they are; they're filled in with numbers or names.
5. Keys ending in `_one` / `_other` are singular / plural forms. If your language doesn't change words for plurals, use the same text for both.

## Try it without building

Drop your file into `%APPDATA%\Jemur\locales\` and restart Jemur. Pick it under **Settings → Language** (or leave it on *Windows display language*). Files there override the built-in ones, so you can also fix a single string this way.

## Send it in

Open a pull request that adds your file to `/locales`. The tests check that every key from `en.json` is present and that placeholders match, so CI will tell you if something is missing.

## Style

- Match Windows' own wording for your language (e.g. what Windows calls *Settings*, *Copy*, *Notification area*).
- Short and plain beats literal. Buttons are verbs.
- Use sentence case unless your language capitalises differently.
