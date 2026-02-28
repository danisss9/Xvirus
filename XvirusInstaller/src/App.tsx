import { FunctionComponent } from 'preact';
import { useState, useEffect } from 'preact/hooks';
import { executeInstall, PRODUCT_INFO } from './services/installer';
import { InstallConfig, InstallProgress } from './model/InstallConfig';
import WelcomeView from './views/WelcomeView';
import ConfigView from './views/ConfigView';
import ProgressView from './views/ProgressView';
import DoneView from './views/DoneView';

type Step = 'welcome' | 'config' | 'progress' | 'done';

const App: FunctionComponent = () => {

  const [currentStep, setCurrentStep] = useState<Step>('welcome');
  const [config, setConfig] = useState<InstallConfig | null>(null);
  const [progress, setProgress] = useState<InstallProgress>({
    step: 'Initializing...',
    progress: 0,
    done: false
  });
  const [installResult, setInstallResult] = useState<{ success: boolean; error?: string } | null>(null);

  const handleWelcomeNext = () => {
    setCurrentStep('config');
  };

  const handleConfigBack = () => {
    setCurrentStep('welcome');
  };

  const handleConfigNext = (newConfig: InstallConfig) => {
    setConfig(newConfig);
    setCurrentStep('progress');
    // Start installation
    executeInstall(newConfig, (p) => setProgress(p))
      .then((result) => {
        setInstallResult(result);
        setCurrentStep('done');
      })
      .catch((error) => {
        setInstallResult({
          success: false,
          error: `Unexpected error: ${error instanceof Error ? error.message : String(error)}`
        });
        setCurrentStep('done');
      });
  };

  const handleProgressComplete = (success: boolean, error?: string) => {
    if (success) {
      setCurrentStep('done');
    } else {
      setCurrentStep('config');
    }
  };

  const handleDoneClick = () => {
    // Window will close naturally through the DoneView buttons
  };

  return (
    <>
      {currentStep === 'welcome' && <WelcomeView onNext={handleWelcomeNext} />}

      {currentStep === 'config' && (
        <ConfigView onNext={handleConfigNext} onBack={handleConfigBack} />
      )}

      {currentStep === 'progress' && config && (
        <ProgressView
          config={config}
          progress={progress}
          onComplete={handleProgressComplete}
        />
      )}

      {currentStep === 'done' && installResult && (
        <DoneView success={installResult.success} error={installResult.error} />
      )}
    </>
  );
};

export default App;
