// Kawaii Island mascots: 20 original skins x 8 expressions, plus the "box" form used for file drops.
// Single source for the HTML mockup (window.Mascot) and SVG export (gen.mjs via require).
// Every skin shares one eye/mouth system so expressions can be swapped (and later animated as XAML layers).
(function (root) {
  const C = { pink:'#FF8FB1', pinkDark:'#E86F96', skin:'#FFE9DC', navy:'#2B2B4A', star:'#FFD166', mouth:'#E85A7A', anger:'#FF5A6E' };
  const eye = (cx, cy, r = 1) => `<ellipse cx="${cx}" cy="${cy}" rx="${4*r}" ry="${5*r}" fill="${C.navy}"/><circle cx="${cx+1.4*r}" cy="${cy-1.9*r}" r="${1.7*r}" fill="#fff"/><circle cx="${cx-1.3*r}" cy="${cy+1.7*r}" r="${.8*r}" fill="#fff"/>`;
  const ln = (d, w = 2, c = C.navy) => `<path d="${d}" stroke="${c}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round" fill="none"/>`;
  const spiral = (cx, y) => `<circle cx="${cx}" cy="${y}" r="4.2" fill="none" stroke="${C.navy}" stroke-width="1.6"/>${ln(`M${cx} ${y-1.8} a1.8 1.8 0 1 1 -1.8 1.8`, 1.4)}`;
  // 5-point star (outer radius R, inner r), pointing up.
  const star = (cx, cy, R, r, fill) => `<path d="M${[...Array(10)].map((_, i) => { const a = Math.PI * (i / 5 - .5), d = i % 2 ? r : R; return `${(cx + d * Math.cos(a)).toFixed(2)} ${(cy + d * Math.sin(a)).toFixed(2)}`; }).join(' L')} Z" fill="${fill}"/>`;
  const sparkle = (x, y, s = 3) => `<path d="M${x} ${y-s} Q${x} ${y} ${x+s} ${y} Q${x} ${y} ${x} ${y+s} Q${x} ${y} ${x-s} ${y} Q${x} ${y} ${x} ${y-s} Z" fill="${C.star}"/>`;

  const EYES = {
    idle: y => eye(24,y) + eye(40,y),
    blink: y => ln(`M20 ${y} Q24 ${y+3} 28 ${y}`) + ln(`M36 ${y} Q40 ${y+3} 44 ${y}`),
    happy: y => ln(`M20 ${y+2} Q24 ${y-4} 28 ${y+2}`) + ln(`M36 ${y+2} Q40 ${y-4} 44 ${y+2}`),
    surprised: y => eye(24,y,1.15) + eye(40,y,1.15),
    sleepy: y => ln(`M20 ${y} Q24 ${y+2.5} 28 ${y}`) + ln(`M36 ${y} Q40 ${y+2.5} 44 ${y}`),
    wow: y => eye(24,y,1.3) + eye(40,y,1.3),                                        // hover: eyes grow
    annoyed: y => ln(`M21 ${y-3} L27 ${y} L21 ${y+3}`) + ln(`M43 ${y-3} L37 ${y} L43 ${y+3}`), // >_<
    dizzy: y => spiral(24,y) + spiral(40,y),
  };
  const MOUTH = {
    idle: y => ln(`M29 ${y} Q32 ${y+3} 35 ${y}`, 1.6),
    blink: y => ln(`M29 ${y} Q32 ${y+3} 35 ${y}`, 1.6),
    happy: y => `<path d="M28 ${y-1} Q32 ${y+6} 36 ${y-1} Z" fill="${C.mouth}" stroke="${C.navy}" stroke-width="1.2" stroke-linejoin="round"/>`,
    surprised: y => `<ellipse cx="32" cy="${y+1}" rx="2" ry="2.6" fill="${C.navy}"/>`,
    sleepy: y => ln(`M30 ${y+1} Q32 ${y} 34 ${y+1}`, 1.4),
    wow: y => `<ellipse cx="32" cy="${y+1}" rx="2.6" ry="2.2" fill="${C.mouth}" stroke="${C.navy}" stroke-width="1.1"/>`,
    annoyed: y => ln(`M29 ${y+1.5} Q32 ${y-1.5} 35 ${y+1.5}`, 1.6),
    dizzy: y => ln(`M28 ${y} Q30 ${y-2} 32 ${y} Q34 ${y+2} 36 ${y}`, 1.5),
  };
  const EXTRA = {
    surprised: `<path d="M57 6 L57 15" stroke="${C.pinkDark}" stroke-width="3" stroke-linecap="round"/><circle cx="57" cy="20" r="1.7" fill="${C.pinkDark}"/>`,
    sleepy: ln('M50 8 h6 l-6 7 h6', 1.6).replace('/>', ' opacity=".6"/>'),
    annoyed: ln('M51 7 q2.5 3 0 6 M59 7 q-2.5 3 0 6 M52 6 q3 2.5 6 0 M52 14 q3 -2.5 6 0', 1.6, C.anger),
    dizzy: sparkle(8, 10) + sparkle(57, 8, 2.5) + sparkle(52, 18, 2),
  };
  const blush = y => `<ellipse cx="17" cy="${y}" rx="3.6" ry="2" fill="${C.pink}" opacity=".65"/><ellipse cx="47" cy="${y}" rx="3.6" ry="2" fill="${C.pink}" opacity=".65"/>`;
  const calm = ex => ex === 'idle' || ex === 'blink';

  // ey/by/my = eye, blush and mouth baselines. back is drawn under the face parts, front over them.
  const SKINS = {
    kiko: { name:'Kiko', kind:'Anime girl', ey:39, by:47, my:48,
      back:`<ellipse cx="32" cy="31" rx="27" ry="25" fill="${C.pinkDark}"/><ellipse cx="9" cy="42" rx="5" ry="11" fill="${C.pinkDark}"/><ellipse cx="55" cy="42" rx="5" ry="11" fill="${C.pinkDark}"/><ellipse cx="32" cy="37" rx="22" ry="20" fill="${C.skin}"/><path d="M9 34 Q10 12 32 10 Q54 12 55 34 Q51 25 45 28 Q41 21 35 27 Q31 20 26 27 Q19 22 15 30 Q12 30 9 34 Z" fill="${C.pink}"/><rect x="44" y="16" width="8" height="3" rx="1.5" fill="#fff" transform="rotate(-25 48 17.5)"/>` },
    miso: { name:'Miso', kind:'Cat', ey:37, by:44, my:46,
      back:`<path d="M11 30 L13 7 L29 20 Z M53 30 L51 7 L35 20 Z" fill="#FFC98B" stroke="#E8954A" stroke-width="1.5" stroke-linejoin="round"/><path d="M15 25 L16 13 L25 20 Z M49 25 L48 13 L39 20 Z" fill="#FF9DB5"/><ellipse cx="32" cy="38" rx="24" ry="20" fill="#FFC98B" stroke="#E8954A" stroke-width="1.5"/>${ln('M32 19 V24 M27 20 L28 24 M37 20 L36 24', 2, '#E8954A')}`,
      front:`<path d="M30.4 42.6 h3.2 l-1.6 1.9 z" fill="#FF7A9A"/>${ln('M5 41 L14 42.5 M5 46 L14 45.5 M59 41 L50 42.5 M59 46 L50 45.5', 1.1).replace('/>', ' opacity=".55"/>')}`,
      mouth: (ex, y) => calm(ex) ? ln(`M28.5 ${y} Q30.2 ${y+2.5} 32 ${y} Q33.8 ${y+2.5} 35.5 ${y}`, 1.5) : null },
    bun: { name:'Bun', kind:'Bunny', ey:40, by:47, my:48,
      back:`<g fill="#FFFFFF" stroke="#F3B6C8" stroke-width="1.5"><ellipse cx="23" cy="16" rx="6.5" ry="14" transform="rotate(-10 23 16)"/><ellipse cx="42" cy="16" rx="6.5" ry="14" transform="rotate(12 42 16)"/></g><ellipse cx="23" cy="17" rx="3" ry="9.5" fill="#FFC2D4" transform="rotate(-10 23 17)"/><ellipse cx="42" cy="17" rx="3" ry="9.5" fill="#FFC2D4" transform="rotate(12 42 17)"/><ellipse cx="32" cy="40" rx="23" ry="19" fill="#FFFFFF" stroke="#F3B6C8" stroke-width="1.5"/>`,
      front:`<ellipse cx="32" cy="45.3" rx="1.7" ry="1.2" fill="${C.pink}"/>`,
      mouth: (ex, y) => calm(ex) ? ln(`M32 ${y-1.6} V${y} M29.5 ${y+0.4} Q30.8 ${y+2} 32 ${y} Q33.2 ${y+2} 34.5 ${y+0.4}`, 1.4) : null },
    bolt: { name:'Bolt', kind:'Robot', ey:34, by:41, my:42,
      back:`${ln('M32 15 V8', 2.2, '#8C98B3')}<circle cx="32" cy="7" r="3.6" fill="${C.pink}"/><rect x="5" y="29" width="6" height="13" rx="2.5" fill="#8C98B3"/><rect x="53" y="29" width="6" height="13" rx="2.5" fill="#8C98B3"/><rect x="9" y="15" width="46" height="40" rx="13" fill="#C9D3E6" stroke="#8C98B3" stroke-width="1.5"/><rect x="14" y="21" width="36" height="28" rx="10" fill="#F2F5FB"/>`,
      front:`<circle cx="15.5" cy="51" r="1.3" fill="#8C98B3"/><circle cx="48.5" cy="51" r="1.3" fill="#8C98B3"/>` },
    ribbit: { name:'Ribbit', kind:'Frog', ey:25, by:42, my:45,
      back:`<g fill="#8FD99B" stroke="#4FAE63" stroke-width="1.5"><circle cx="24" cy="25" r="9.5"/><circle cx="40" cy="25" r="9.5"/><ellipse cx="32" cy="41" rx="26" ry="16"/></g><ellipse cx="32" cy="41" rx="24.5" ry="14.5" fill="#8FD99B"/><circle cx="24" cy="25" r="6.8" fill="#fff"/><circle cx="40" cy="25" r="6.8" fill="#fff"/>`,
      mouth: (ex, y) => calm(ex) ? ln(`M23 ${y} Q32 ${y+6} 41 ${y}`, 1.6) : null },

    // --- Hero crew: original characters that nod to an archetype (no names, logos or costumes copied) ---
    valor: { name:'Valor', kind:'Star pup', ey:40, by:47, my:49,
      back:`<ellipse cx="11" cy="39" rx="6" ry="12" fill="#3E5BA9" transform="rotate(14 11 39)"/><ellipse cx="53" cy="39" rx="6" ry="12" fill="#3E5BA9" transform="rotate(-14 53 39)"/><ellipse cx="32" cy="39" rx="22" ry="19" fill="#F6E3CC" stroke="#C9A27E" stroke-width="1.5"/><path d="M10 34 Q10 12 32 11 Q54 12 54 34 Q44 27 32 27 Q20 27 10 34 Z" fill="#3E5BA9"/>${star(32, 19.5, 5.2, 2.2, '#FFFFFF')}` },
    rumble: { name:'Rumble', kind:'Thunder bear', ey:39, by:46, my:48,
      back:`<circle cx="14" cy="19" r="7.5" fill="#D9A24E" stroke="#A9762E" stroke-width="1.5"/><circle cx="50" cy="19" r="7.5" fill="#D9A24E" stroke="#A9762E" stroke-width="1.5"/><circle cx="14" cy="19" r="4" fill="#F2C98A"/><circle cx="50" cy="19" r="4" fill="#F2C98A"/><ellipse cx="32" cy="38" rx="23" ry="20" fill="#E8B86B" stroke="#A9762E" stroke-width="1.5"/><ellipse cx="32" cy="45" rx="11" ry="8" fill="#FBE3B8"/><path d="M33.5 15 L28 25 L32 25 L30 32 L37.5 21.5 L33.5 21.5 L36 15 Z" fill="#FFD166" stroke="#C99A2E" stroke-width="1.1" stroke-linejoin="round"/>` },
    forge: { name:'Forge', kind:'Armor bot', ey:37, by:44, my:46,
      back:`<rect x="9" y="12" width="46" height="44" rx="16" fill="#D94040" stroke="#9E2A2A" stroke-width="1.5"/><rect x="15" y="25" width="34" height="27" rx="10" fill="#F2C14E" stroke="#C9952A" stroke-width="1.2"/><circle cx="32" cy="18.5" r="3.6" fill="#BFF3FF" stroke="#4CC9F0" stroke-width="1.6"/>` },
    brick: { name:'Brick', kind:'Big green buddy', ey:40, by:47, my:49,
      back:`<ellipse cx="32" cy="39" rx="26" ry="19" fill="#7DC46B" stroke="#4E9A45" stroke-width="1.5"/><path d="M8 33 Q8 14 32 13 Q56 14 56 33 L50 29 L45 32 L40 28 L35 32 L29 28 L24 32 L19 28 L14 32 Z" fill="#3A3A4A"/><rect x="8" y="29" width="48" height="4.5" rx="2.2" fill="#7B5BB5"/>` },
    trick: { name:'Trick', kind:'Mischief goat', ey:40, by:47, my:49,
      back:`<path d="M21 22 Q10 3 3 14 Q12 10 16 26 Z M43 22 Q54 3 61 14 Q52 10 48 26 Z" fill="#E8B94A" stroke="#B8862A" stroke-width="1.3" stroke-linejoin="round"/><ellipse cx="8" cy="36" rx="7" ry="3.6" fill="#E9E2CB" transform="rotate(20 8 36)"/><ellipse cx="56" cy="36" rx="7" ry="3.6" fill="#E9E2CB" transform="rotate(-20 56 36)"/><ellipse cx="32" cy="39" rx="22" ry="19" fill="#F4F1E6" stroke="#C9C2A8" stroke-width="1.5"/><path d="M11 31 Q14 16 32 15 Q50 16 53 31 Q44 22 32 25 Q20 22 11 31 Z" fill="#2F6B4F"/>` },
    gloom: { name:'Gloom', kind:'Iron owl', ey:38, by:45, my:50,
      back:`<path d="M8 22 L13 5 L22 17 Z M56 22 L51 5 L42 17 Z" fill="#3F7A4A"/><ellipse cx="32" cy="35" rx="28" ry="27" fill="#3F7A4A"/><ellipse cx="32" cy="38" rx="22" ry="21" fill="#2A4F31"/><ellipse cx="32" cy="40" rx="19" ry="16" fill="#C3CBD4" stroke="#8C96A3" stroke-width="1.4"/>`,
      front:`<path d="M29.8 43.6 L32 47.2 L34.2 43.6 Z" fill="#F2B544" stroke="#C98A2A" stroke-width=".8" stroke-linejoin="round"/>`,
      mouth: (ex, y) => calm(ex) ? ' ' : null }, // calm owl: the beak is the mouth
    scruff: { name:'Scruff', kind:'Grumpy wolverine', ey:39, by:46, my:48,
      back:`<path d="M11 31 L4 5 L23 20 Z M53 31 L60 5 L41 20 Z" fill="#2E2A33"/><circle cx="16" cy="22" r="5" fill="#8A5A3C"/><circle cx="48" cy="22" r="5" fill="#8A5A3C"/><ellipse cx="32" cy="39" rx="23" ry="19" fill="#8A5A3C" stroke="#5E3B26" stroke-width="1.5"/><ellipse cx="32" cy="43" rx="16.5" ry="13" fill="#E9C9A1"/>` },

    // --- Anime crew ---
    sunny: { name:'Sunny', kind:'Straw-hat monkey', ey:40, by:47, my:49,
      back:`<circle cx="9" cy="39" r="7" fill="#A8744F"/><circle cx="55" cy="39" r="7" fill="#A8744F"/><circle cx="9" cy="39" r="4" fill="#F5D2B0"/><circle cx="55" cy="39" r="4" fill="#F5D2B0"/><ellipse cx="32" cy="39" rx="21" ry="19" fill="#A8744F" stroke="#7A5134" stroke-width="1.4"/><ellipse cx="32" cy="42" rx="16" ry="14" fill="#F5D2B0"/><ellipse cx="32" cy="21" rx="27" ry="6" fill="#F2D27A" stroke="#C9A24A" stroke-width="1.3"/><path d="M17 21 Q17 6 32 6 Q47 6 47 21 Z" fill="#F2D27A" stroke="#C9A24A" stroke-width="1.3"/><rect x="17" y="15" width="30" height="4.5" fill="#4A7BD1"/>` },
    kit: { name:'Kit', kind:'Ninja fox', ey:39, by:46, my:48,
      back:`<path d="M10 32 L12 5 L28 19 Z M54 32 L52 5 L36 19 Z" fill="#F5A04A" stroke="#D9782A" stroke-width="1.4" stroke-linejoin="round"/><path d="M14 25 L15 12 L23 19 Z M50 25 L49 12 L41 19 Z" fill="#FFD8B0"/><ellipse cx="32" cy="39" rx="24" ry="19" fill="#F5A04A" stroke="#D9782A" stroke-width="1.4"/><ellipse cx="32" cy="46" rx="12" ry="8" fill="#FFF3E6"/><rect x="8" y="24" width="48" height="6" rx="3" fill="#3B4E8C"/><rect x="24" y="22.5" width="16" height="9" rx="2" fill="#D4DAE3" stroke="#8C96A3" stroke-width="1"/>`,
      front: ln('M9 42 L15 43 M9 45.5 L15 45.5 M9 49 L15 48 M55 42 L49 43 M55 45.5 L49 45.5 M55 49 L49 48', 1.3, '#7A3E14') },
    snow: { name:'Snow', kind:'Cool snow cat', ey:40, by:47, my:49,
      back:`<path d="M10 32 L12 8 L27 20 Z M54 32 L52 8 L37 20 Z" fill="#FFFFFF" stroke="#C7D3E3" stroke-width="1.4" stroke-linejoin="round"/><path d="M14 26 L15 14 L23 20 Z M50 26 L49 14 L41 20 Z" fill="#BFE3FF"/><ellipse cx="32" cy="40" rx="23" ry="18" fill="#FFFFFF" stroke="#C7D3E3" stroke-width="1.4"/><path d="M14 30 L18 14 L23 25 L27 10 L32 23 L37 10 L41 25 L46 14 L50 30 Q32 24 14 30 Z" fill="#F4F7FC" stroke="#C7D3E3" stroke-width="1.2" stroke-linejoin="round"/><circle cx="26" cy="27.5" r="4" fill="#2B2B4A"/><circle cx="38" cy="27.5" r="4" fill="#2B2B4A"/>${ln('M30 27.5 L34 27.5', 1.4)}` },
    rosy: { name:'Rosy', kind:'Spiky-hair kid', ey:39, by:47, my:48,
      back:`<ellipse cx="32" cy="37" rx="22" ry="20" fill="#FFE3D3"/><path d="M9 36 Q8 18 16 13 L14 22 Q20 10 28 9 L25 17 Q32 6 40 9 L37 15 Q46 9 50 14 L46 19 Q56 20 55 36 Q50 26 44 27 L43 22 Q36 27 30 25 L28 20 Q22 26 17 25 L16 29 Q12 30 9 36 Z" fill="#FF7FA8" stroke="#E0567F" stroke-width="1.2" stroke-linejoin="round"/><rect x="9" y="30" width="5" height="12" rx="2.5" fill="#3A3036"/><rect x="50" y="30" width="5" height="12" rx="2.5" fill="#3A3036"/>` },
    grit: { name:'Grit', kind:'Brave hamster', ey:39, by:46, my:48,
      back:`<circle cx="13" cy="21" r="6.5" fill="#F2CFA0" stroke="#C99A62" stroke-width="1.4"/><circle cx="51" cy="21" r="6.5" fill="#F2CFA0" stroke="#C99A62" stroke-width="1.4"/><circle cx="13" cy="21" r="3.4" fill="#FFB8A8"/><circle cx="51" cy="21" r="3.4" fill="#FFB8A8"/><path d="M10 55 Q32 64 54 55 L55 62 Q32 70 9 62 Z" fill="#3E7A55"/><ellipse cx="32" cy="38" rx="23" ry="19" fill="#F2CFA0" stroke="#C99A62" stroke-width="1.4"/><path d="M12 30 Q14 16 32 15 Q50 16 52 30 Q46 22 38 26 Q34 20 28 25 Q20 21 12 30 Z" fill="#6B4430"/><circle cx="32" cy="14" r="4.5" fill="#6B4430"/>` },
    tidy: { name:'Tidy', kind:'Neat kitten', ey:39, by:46, my:48,
      back:`<path d="M24 54 Q32 50 40 54 L36 61 Q32 58 28 61 Z" fill="#FFFFFF" stroke="#CFCFDA" stroke-width="1.2" stroke-linejoin="round"/><path d="M10 31 L12 8 L27 20 Z M54 31 L52 8 L37 20 Z" fill="#2E2E38" stroke="#1C1C24" stroke-width="1.2" stroke-linejoin="round"/><ellipse cx="32" cy="37" rx="23" ry="18" fill="#2E2E38" stroke="#1C1C24" stroke-width="1.4"/><ellipse cx="32" cy="41" rx="17" ry="13" fill="#F3EEE8"/><path d="M15 34 Q17 22 32 21 Q47 22 49 34 Q40 28 33 29 L32 25 Q24 27 15 34 Z" fill="#2E2E38"/>` },
    sprout: { name:'Sprout', kind:'Spiky pup', ey:40, by:47, my:49,
      back:`<path d="M11 36 L6 18 L15 24 L13 6 L22 18 L26 2 L32 16 L38 2 L42 18 L51 6 L49 24 L58 18 L53 36 Z" fill="#2E3A2E" stroke="#1E281E" stroke-width="1.2" stroke-linejoin="round"/><path d="M13 6 L15.5 12 L11.5 10 Z M26 2 L28 9 L24 7 Z M38 2 L40 7 L36 9 Z M51 6 L52.5 10 L48.5 12 Z" fill="#5FBF6A"/><ellipse cx="32" cy="40" rx="21" ry="18" fill="#FFE3CF"/><path d="M12 36 Q16 26 24 30 Q28 24 32 29 Q36 24 40 30 Q48 26 52 36 Q44 30 32 32 Q20 30 12 36 Z" fill="#2E3A2E"/>` },
    clover: { name:'Clover', kind:'Freckled bunny', ey:40, by:47, my:48,
      back:`<g fill="#E9D3B4" stroke="#C4A57E" stroke-width="1.4"><ellipse cx="22" cy="16" rx="6" ry="13" transform="rotate(-12 22 16)"/><ellipse cx="42" cy="16" rx="6" ry="13" transform="rotate(12 42 16)"/></g><ellipse cx="32" cy="40" rx="23" ry="19" fill="#FFF4E6" stroke="#C4A57E" stroke-width="1.4"/><g fill="#2F5D4A"><circle cx="24" cy="25" r="5"/><circle cx="31" cy="22.5" r="5.5"/><circle cx="38.5" cy="24" r="5"/><circle cx="44" cy="28" r="4"/><circle cx="19.5" cy="29" r="4"/><circle cx="28" cy="27.5" r="3.6"/><circle cx="35.5" cy="28" r="3.6"/></g>`,
      front:`<g fill="#C9875A" opacity=".75"><circle cx="15.5" cy="44.5" r=".9"/><circle cx="18.5" cy="45.8" r=".9"/><circle cx="16.5" cy="47.8" r=".9"/><circle cx="48.5" cy="44.5" r=".9"/><circle cx="45.5" cy="45.8" r=".9"/><circle cx="47.5" cy="47.8" r=".9"/></g>` },
  };

  // Coding / lock-in outfit: hoodie (hood frames the head, shoulders + drawstrings below) and nerdy glasses.
  const HOOD = '#3B4A6B', HOOD_IN = '#26314A';
  const hoodie = `<path d="M3 64 Q4 50 15 47 L49 47 Q60 50 61 64 Z" fill="${HOOD}"/><ellipse cx="32" cy="33" rx="30.5" ry="29" fill="${HOOD}"/><ellipse cx="32" cy="35" rx="26" ry="24.5" fill="${HOOD_IN}"/>`;
  const strings = `${ln('M26 57 V62', 1.4, '#E8ECF5')}${ln('M38 57 V62', 1.4, '#E8ECF5')}`;
  const lens = (x, y, paint) => `<rect x="${x}" y="${y-6.5}" width="15" height="13" rx="4.5" ${paint}/>`;
  const glasses = y => [16.5, 32.5].map(x => lens(x, y, 'fill="#A8DCFF" opacity=".28"') + lens(x, y, `fill="none" stroke="${C.navy}" stroke-width="1.8"`)).join('') +
    ln(`M31.5 ${y-1.5} Q32 ${y-2.6} 32.5 ${y-1.5}`, 1.6) + ln(`M16.5 ${y-2} L11 ${y-4}`, 1.6) + ln(`M47.5 ${y-2} L53 ${y-4}`, 1.6);

  // A face in three layers so the app can slide the eyes toward the cursor: under (head), eyes, over (rest).
  function layers(skin, ex = 'idle', outfit = '') {
    const s = SKINS[skin] || SKINS.kiko;
    const mouth = (s.mouth && s.mouth(ex, s.my)) || MOUTH[ex](s.my);
    const code = outfit === 'code';
    return { under: (code ? hoodie : '') + s.back, eyes: EYES[ex](s.ey),
             over: blush(s.by) + mouth + (s.front || '') + (code ? strings + glasses(s.ey) : '') + (EXTRA[ex] || '') };
  }
  const svg = body => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" aria-hidden="true">${body}</svg>`;

  function face(skin, ex = 'idle', outfit = '') {
    const l = layers(skin, ex, outfit);
    return svg(l.under + l.eyes + l.over);
  }

  // File-drop form: the mascot turns into a little box. open = waiting for the drop, closed = swallowed.
  function box(open = true) {
    const lid = open
      ? `<path d="M12 29 L3 20 L21 17 L29 28 Z M52 29 L61 20 L43 17 L35 28 Z" fill="#F2CB9E" stroke="#B9824F" stroke-width="1.4" stroke-linejoin="round"/>`
      : `<rect x="9" y="23" width="46" height="9" rx="2.5" fill="#F2CB9E" stroke="#B9824F" stroke-width="1.4"/><rect x="28" y="23" width="8" height="9" fill="#FFE3A3"/>`;
    const faceParts = open
      ? eye(24, 41, .8) + eye(40, 41, .8) + `<ellipse cx="32" cy="49" rx="3" ry="3.4" fill="${C.navy}"/>`
      : EYES.happy(42) + MOUTH.happy(49);
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" aria-hidden="true"><rect x="12" y="28" width="40" height="28" rx="3.5" fill="#E8B98A" stroke="#B9824F" stroke-width="1.4"/>${lid}${faceParts}${blush(48)}</svg>`;
  }

  // Composable pieces for the macOS app (gen.mjs → MascotData.swift), assembled exactly like layers().
  function parts() {
    const skins = {};
    for (const [k, s] of Object.entries(SKINS)) {
      const faces = {};
      for (const ex of Object.keys(EYES))
        faces[ex] = { eyes: EYES[ex](s.ey), over: blush(s.by) + ((s.mouth && s.mouth(ex, s.my)) || MOUTH[ex](s.my)) + (s.front || '') };
      skins[k] = { name: s.name, kind: s.kind, back: s.back, glasses: strings + glasses(s.ey), faces };
    }
    return { skins, extra: EXTRA, hoodie };
  }

  const api = { face, layers, parts, svg, box, SKINS, EXPRESSIONS: Object.keys(EYES), TRACKING: ['idle', 'surprised', 'wow'] };
  if (typeof module === 'object' && module.exports) module.exports = api; else root.Mascot = api;
})(this);
