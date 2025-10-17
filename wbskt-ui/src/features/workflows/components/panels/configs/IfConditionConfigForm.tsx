import { useForm } from 'react-hook-form';
import type { IfConditionConfiguration } from '../../../../../types/api';
import { Label } from '../../../../../components/common/Label';
import { Input } from '../../../../../components/common/Input';

interface IfConditionConfigFormProps {
  configuration: IfConditionConfiguration;
  onChange: (newConfig: IfConditionConfiguration) => void;
}

export const IfConditionConfigForm = ({ configuration, onChange }: IfConditionConfigFormProps) => {
  const { register, watch } = useForm<IfConditionConfiguration>({ defaultValues: configuration });

  watch((value) => {
    onChange(value as IfConditionConfiguration);
  });

  return (
    <form className="space-y-4">
      <div>
        <Label htmlFor="leftOperand">Left Operand</Label>
        <Input id="leftOperand" type="text" {...register('leftOperand', { required: true })} placeholder="{{ context.trigger.body.value }}" />
      </div>
      <div>
        <Label htmlFor="operator">Operator</Label>
        <Input id="operator" type="text" {...register('operator', { required: true })} placeholder="GreaterThan" />
      </div>
      <div>
        <Label htmlFor="rightOperand">Right Operand</Label>
        <Input id="rightOperand" type="text" {...register('rightOperand', { required: true })} placeholder="40" />
      </div>
    </form>
  );
};
