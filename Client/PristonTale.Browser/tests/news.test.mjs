import test from 'node:test';
import assert from 'node:assert/strict';
import { newsDay, shownAddress } from '../wwwroot/news.js';

test('the news window shows the post day as players read it and community addresses without scheme or closing slash', () => {
    assert.equal(newsDay('2026-11-18'), '18/11/2026');
    assert.equal(newsDay('ontem'), 'ontem');
    assert.equal(shownAddress('https://discord.com/invite/4FdF7Mes2B'), 'discord.com/invite/4FdF7Mes2B');
    assert.equal(shownAddress('https://www.instagram.com/noxiustale/'), 'www.instagram.com/noxiustale');
});
