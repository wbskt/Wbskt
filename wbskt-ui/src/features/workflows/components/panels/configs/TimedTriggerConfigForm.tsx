import { useForm } from 'react-hook-form';
import type { TimedTriggerConfiguration } from '../../../../../types/api';
import { Label } from '../../../../../components/common/Label';
import { Input } from '../../../../../components/common/Input';

interface TimedTriggerConfigFormProps {
  configuration: TimedTriggerConfiguration;
  onChange: (newConfig: TimedTriggerConfiguration) => void;
}

export const TimedTriggerConfigForm = ({ configuration, onChange }: TimedTriggerConfigFormProps) => {
  const { register, watch } = useForm<TimedTriggerConfiguration>({ defaultValues: configuration });

  // Subscribe to form changes and propagate them upwards
  watch((value) => {
    onChange(value as TimedTriggerConfiguration);
  });

  return (
    <form className="space-y-4">
      <div>
        <Label htmlFor="cronExpression">CRON Schedule</Label>
        <Input id="cronExpression" type="text" {...register('cronExpression', { required: true })} />
        <p className="text-xs text-gray-500 mt-1">e.g., '0 * * * *' for every hour.</p>
      </div>
    </form>
  );
};