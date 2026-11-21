import spanish from './languages/es.js';
import filipino from './languages/fil.js';
import vietnamese from './languages/vi.js';
import simplifiedChinese from './languages/zh-CN.js';
import japanese from './languages/ja.js';

// The page's languages (ClientSettings.SupportedLanguages), each named in itself. Portuguese and English are written
// beside each phrase: a say(pt, en) phrase, or a table per language. Every other language is a catalog of its own in
// languages/<code>.js: its phrases by their English words, and its tables by name (main's, heroes', ...).
export const pageLanguages = [['pt-BR', 'Português'], ['en', 'English'], ['es', 'Español'], ['fil', 'Filipino'], ['vi', 'Tiếng Việt'], ['zh-CN', '简体中文'], ['ja', '日本語']];
export const defaultLanguage = 'pt-BR';
export const catalogs = { es: spanish, fil: filipino, vi: vietnamese, 'zh-CN': simplifiedChinese, ja: japanese };

// The catalog keys of every say() phrase made, for the catalogs' test.
export const askedPhrases = new Set();

// key: the phrase's catalog key when its English words mean something else elsewhere (default: the English words).
export function say(pt, en, key = en) {
    askedPhrases.add(key);
    return key === en ? { 'pt-BR': pt, en } : { 'pt-BR': pt, en, key };
}

export const isPageLanguage = language => pageLanguages.some(([code]) => code === language);

// A phrase in a language: written beside it, else from the language's catalog by its English words, else in English.
export function word(phrase, language) {
    if (phrase === null || phrase === undefined) return '';
    if (typeof phrase === 'string') return phrase;
    return phrase[language] ?? catalogs[language]?.phrases?.[phrase.key ?? phrase.en] ?? phrase.en ?? phrase[defaultLanguage] ?? '';
}

// A table of texts per language: its own, else the language's catalog's table of that name, else the English one.
export function table(tables, name, language) {
    return tables[language] ?? catalogs[language]?.tables?.[name] ?? tables.en;
}
