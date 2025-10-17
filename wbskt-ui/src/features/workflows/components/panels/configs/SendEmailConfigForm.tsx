import { useForm } from 'react-hook-form';
import type { SendEmailConfiguration } from '../../../../../types/api';
import { Input } from '../../../../../components/common/Input';
import { Label } from '../../../../../components/common/Label';

interface SendEmailConfigFormProps {
  configuration: SendEmailConfiguration;
  onChange: (newConfig: SendEmailConfiguration) => void;
}

export const SendEmailConfigForm = ({ configuration, onChange }: SendEmailConfigFormProps) => {
  const { register, watch } = useForm<SendEmailConfiguration>({ defaultValues: configuration });

  watch((value) => {
    onChange(value as SendEmailConfiguration);
  });

  return (
    <form className="space-y-4">
      <div>
        <Label htmlFor="integrationName">Integration Name</Label>
        <Input id="integrationName" type="text" {...register('integrationName', { required: true })} />
      </div>
      <div>
        <Label htmlFor="to">To</Label>
        <Input id="to" type="email" {...register('to', { required: true })} />
      </div>
      <div>
        <Label htmlFor="subject">Subject</Label>
        <Input id="subject" type="text" {...register('subject', { required: true })} />
      </div>
      <div>
        <Label htmlFor="body">Body</Label>
        <textarea id="body" {...register('body')} className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm" rows={4} />
      </div>
    </form>
  );
};
