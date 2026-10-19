// Scenes 1 to 5 are one continuous world (World.tsx); each scene adds its own words on top.
// Every scene is a component here, with all of its timing taken from config.ts.
import React from 'react';
import { beats as b, color, scenes, showBatteryBeat, words } from '../config';
import { Words } from '../components/Type';
import { batteryShift } from '../tracks';
import type { Layout } from '../World';

type P = { t: number; L: Layout };
const Slot: React.FC<{ L: Layout; dy?: number; children: React.ReactNode }> = ({ L, dy = 0, children }) => (
  <div style={{ position: 'absolute', left: L.captionX, width: L.captionW, top: L.captionY + dy * (L.landscape ? 1.22 : 1) }}>{children}</div>
);

/** 1. The hook: the lid slams, and the words punch in. */
export const Hook: React.FC<P> = ({ t, L }) => (
  <>
    <Slot L={L}>
      <Words text={words.hook1} t={t} start={b.hookLine1} end={scenes.hook.end - 0.1} size={104} punch stagger={0.12} />
    </Slot>
    <Slot L={L} dy={L.landscape ? 236 : 128}>
      <Words text={words.hook2} t={t} start={b.hookLine2} end={scenes.hook.end - 0.05} size={64} weight={600} ink={color.text2} />
    </Slot>
  </>
);

/** 2. The problem: Windows suspends the terminal. Morning, and nothing moved. */
export const Problem: React.FC<P> = ({ t, L }) => (
  <>
    <Slot L={L}>
      <Words text={words.suspend} t={t} start={b.suspendLabel} end={b.nothingDone - 0.1} size={64} weight={600} fromLeft maxWidth={820} />
    </Slot>
    <Slot L={L}>
      <Words text={words.nothing} t={t} start={b.nothingDone} end={scenes.problem.end + 0.15} size={72} />
    </Slot>
  </>
);

/** 3. The rewind gag. */
export const Rewind: React.FC<P> = ({ t, L }) => (
  <Slot L={L}>
    <Words text={words.again} t={t} start={b.againWords} end={scenes.rewind.end - 0.05} size={80} stagger={0.2} />
  </Slot>
);

/** 4. Vigil arrives. */
export const Enter: React.FC<P> = ({ t, L }) => (
  <Slot L={L}>
    <Words text={words.watch} t={t} start={b.watchLine} end={scenes.enter.end} size={68} maxWidth={860} />
  </Slot>
);

/** 6. Morning: the lid opens on the desktop and Vigil says welcome back. */
export const Morning: React.FC<P> = ({ t, L }) => (
  <Slot L={L}>
    <Words text={words.general} t={t} start={b.generalCaption} end={b.zoomScreen + 0.35} size={72} maxWidth={900} />
  </Slot>
);

/** 5. The night shift. */
export const Night: React.FC<P> = ({ t, L }) => {
  const s = batteryShift;
  return (
    <>
      <Slot L={L}><Words text={words.done} t={t} start={b.docsDone + 0.1} end={b.pipelineDone - 0.1} size={72} ink={color.green} /></Slot>
      <Slot L={L}><Words text={words.hot} t={t} start={b.heatRise + 0.1} end={b.coolDown - 0.05} size={72} ink={color.hotA} /></Slot>
      <Slot L={L}><Words text={words.cool} t={t} start={b.coolDown + 0.15} end={b.coolDown + 1.7} size={72} ink={color.cool} /></Slot>
      {showBatteryBeat ? <Slot L={L}><Words text={words.battery} t={t} start={b.coolDown + 1.8} end={b.coolDown + 3.6} size={64} /></Slot> : null}
      <Slot L={L}><Words text={words.needs} t={t} start={b.needsYou + s + 0.2} end={b.phoneIn + s - 0.05} size={72} ink={color.amberA} /></Slot>
      <Slot L={L}><Words text={words.wait} t={t} start={b.waitHour + s + 0.05} end={b.toDawn + s} size={68} maxWidth={900} /></Slot>
    </>
  );
};
