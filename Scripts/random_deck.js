const fs = require('fs');
const path = require('path');

// Paths
const cardCsvPath = path.join(__dirname, '..', 'DataBase', 'Card', '重剑手Card.csv');
const deckCsvPath = path.join(__dirname, '..', 'DataBase', 'Unit', 'Character', 'CharacterDefaultDeck.csv');

// Read 重剑手Card.csv
const cardLines = fs.readFileSync(cardCsvPath, 'utf-8').split('\n').filter(line => line.trim());
const cardIds = [];
for (let i = 1; i < cardLines.length; i++) {
    const cols = cardLines[i].split(',');
    if (cols.length >= 1 && cols[0].trim()) {
        cardIds.push(cols[0].trim());
    }
}

console.log(`Total available card IDs: ${cardIds.length}`);

// Requirements:
// - Character ID: 1002
// - All card IDs from 重剑手Card.csv
// - Total count: 15-20 (min 15, max 20)
// - As many different card types as possible -> pick 15-20 unique cards, each with count 1
const CHARACTER_ID = '1002';
const MIN_TOTAL = 15;
const MAX_TOTAL = 20;

// Shuffle card IDs for randomness
function shuffle(arr) {
    for (let i = arr.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [arr[i], arr[j]] = [arr[j], arr[i]];
    }
    return arr;
}

// Decide how many unique cards to pick (between MIN_TOTAL and MAX_TOTAL)
// Since we want max variety, each unique card gets count 1
const pickCount = Math.floor(Math.random() * (MAX_TOTAL - MIN_TOTAL + 1)) + MIN_TOTAL;
const shuffled = shuffle([...cardIds]);
const selectedIds = shuffled.slice(0, Math.min(pickCount, shuffled.length));

console.log(`Target total cards: ${pickCount}`);
console.log(`Selected ${selectedIds.length} unique card types:\n`);

const result = [];
for (const cardId of selectedIds) {
    result.push({ CharacterId: CHARACTER_ID, CardId: cardId, Count: 1 });
}

// Print selected cards
let totalCount = 0;
for (const r of result) {
    console.log(`  ${r.CardId} x ${r.Count}`);
    totalCount += r.Count;
}
console.log(`\nTotal cards for character ${CHARACTER_ID}: ${totalCount}`);

// Read existing CharacterDefaultDeck.csv
const existingLines = fs.readFileSync(deckCsvPath, 'utf-8').split('\n').filter(line => line.trim());
const existingHeader = existingLines[0];

// Keep rows for other characters, remove all rows for 1002
const newLines = [existingHeader];
for (let i = 1; i < existingLines.length; i++) {
    const cols = existingLines[i].split(',');
    if (cols.length >= 1 && cols[0].trim() !== CHARACTER_ID) {
        newLines.push(existingLines[i]);
    }
}

// Add new deck rows for 1002
for (const r of result) {
    newLines.push(`${r.CharacterId},${r.CardId},${r.Count}`);
}

// Write back
fs.writeFileSync(deckCsvPath, newLines.join('\n') + '\n', 'utf-8');
console.log(`\n✅ Successfully updated ${deckCsvPath}`);