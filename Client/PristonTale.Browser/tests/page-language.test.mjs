import test from 'node:test';
import assert from 'node:assert/strict';
import { askedPhrases, catalogs, pageLanguages, say, table, word } from '../wwwroot/page-language.js';
import '../wwwroot/settings-options.js';
import '../wwwroot/settings.js';
import '../wwwroot/news.js';
import { heroTables } from '../wwwroot/heroes.js';
import { touchTables } from '../wwwroot/touch-hud.js';
import { worldTables } from '../wwwroot/world-hud.js';
import { loginTexts } from '../wwwroot/login-texts.js';

const tables = { ...heroTables, ...touchTables, ...worldTables, login: loginTexts };
const placeholders = text => (text.match(/\{\d+\}|%|\$\{[^}]*\}/g) ?? []).sort();

// The keys, array lengths and function arities of a table, for comparing a catalog's table with the English one.
function shape(value) {
    if (typeof value === 'function') return `function/${value.length}`;
    if (Array.isArray(value)) return value.map(shape);
    if (value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(key => [key, shape(value[key])]));
    return typeof value;
}

test('every language but Portuguese and English has a catalog, and the page offers each named in itself', () => {
    assert.deepEqual(pageLanguages.map(([code]) => code), ['pt-BR', 'en', ...Object.keys(catalogs)]);
});

test('every catalog words every phrase and every table of the page, keeping placeholders', () => {
    assert.ok(askedPhrases.size > 300, 'the page phrases were not collected');
    for (const [language, catalog] of Object.entries(catalogs)) {
        const missing = [...askedPhrases].filter(phrase => !catalog.phrases[phrase]?.trim());
        assert.deepEqual(missing, [], `${language} phrases missing`);
        const changed = [...askedPhrases].filter(phrase => placeholders(phrase).join() !== placeholders(catalog.phrases[phrase]).join());
        assert.deepEqual(changed, [], `${language} phrases whose placeholders differ`);
        for (const [name, perLanguage] of Object.entries(tables)) {
            assert.deepEqual(shape(catalog.tables[name]), shape(perLanguage.en), `${language} table ${name} differs from English`);
        }
    }
});

test('a phrase shows in its language, from the catalog by its English words, or in English', () => {
    const phrase = say('Salvar', 'Save');
    assert.equal(word(phrase, 'pt-BR'), 'Salvar');
    assert.equal(word(phrase, 'en'), 'Save');
    assert.equal(word(phrase, 'es'), catalogs.es.phrases.Save);
    assert.equal(word({ 'pt-BR': 'Só português', en: 'Not in any catalog' }, 'es'), 'Not in any catalog');
    assert.equal(word('as is', 'es'), 'as is');
    assert.equal(table(touchTables.touch, 'touch', 'es'), catalogs.es.tables.touch);
    assert.equal(table(touchTables.touch, 'touch', 'fr'), touchTables.touch.en);
});
