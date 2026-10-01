// Kawaii Island mascots: 5 original skins x 8 expressions, plus the "box" form used for file drops.
// Single source for the HTML mockup (window.Mascot) and SVG export (gen.mjs via require).
// Every skin shares one eye/mouth system so expressions can be swapped (and later animated as XAML layers).
(function (root) {
  const C = { pink:'#FF8FB1', pinkDark:'#E86F96', skin:'#FFE9DC', navy:'#2B2B4A', star:'#FFD166', mouth:'#E85A7A', anger:'#FF5A6E' };
  const eye = (cx, cy, r = 1) => `<ellipse cx="${cx}" cy="${cy}" rx="${4*r}" ry="${5*r}" fill="${C.navy}"/><circle cx="${cx+1.4*r}" cy="${cy-1.9*r}" r="${1.7*r}" fill="#fff"/><circle cx="${cx-1.3*r}" cy="${cy+1.7*r}" r="${.8*r}" fill="#fff"/>`;
  const ln = (d, w = 2, c = C.navy) => `<path d="${d}" stroke="${c}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round" fill="none"/>`;
  const spiral = (cx, y) => `<circle cx="${cx}" cy="${y}" r="4.2" fill="none" stroke="${C.navy}" stroke-width="1.6"/>${ln(`M${cx} ${y-1.8} a1.8 1.8 0 1 1 -1.8 1.8`, 1.4)}`;
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
  };

  function face(skin, ex = 'idle') {
    const s = SKINS[skin] || SKINS.kiko;
    const mouth = (s.mouth && s.mouth(ex, s.my)) || MOUTH[ex](s.my);
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" aria-hidden="true">${s.back}${EYES[ex](s.ey)}${blush(s.by)}${mouth}${s.front || ''}${EXTRA[ex] || ''}</svg>`;
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

  const api = { face, box, SKINS, EXPRESSIONS: Object.keys(EYES) };
  if (typeof module === 'object' && module.exports) module.exports = api; else root.Mascot = api;
})(this);
