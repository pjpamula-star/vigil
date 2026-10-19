import React from 'react';
import { Composition, Still } from 'remotion';
import { DURATION, FPS } from './config';
import { Main, ReadmeCut, readmeDuration } from './Main';
import { CharacterSheet } from './CharacterSheet';
import { Cover, Social } from './Stills';

export const RemotionRoot: React.FC = () => (
  <>
    <Composition id="LinkedIn" component={Main} durationInFrames={Math.round(DURATION * FPS)} fps={FPS} width={1080} height={1350} defaultProps={{ blur: true }} />
    <Composition id="X" component={Main} durationInFrames={Math.round(DURATION * FPS)} fps={FPS} width={1920} height={1080} defaultProps={{ blur: true }} />
    <Composition id="Readme" component={ReadmeCut} durationInFrames={Math.round(readmeDuration() * FPS)} fps={FPS} width={1080} height={1350} />
    <Still id="Cover" component={Cover} width={1080} height={1350} />
    <Still id="Social" component={Social} width={1280} height={640} />
    <Still id="CharacterSheet" component={CharacterSheet} width={1080} height={760} />
  </>
);
