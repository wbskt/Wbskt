import { useForm } from 'react-hook-form';
import { Label } from '../../../components/common/Label';
import { Input } from '../../../components/common/Input';
import { Button } from '../../../components/common/Button';

export type CredentialFormData = {
  name: string;
  integrationType: string;
  credentials: string;
};

interface CredentialFormProps {
  onSubmit: (data: CredentialFormData) => void;
  isSubmitting: boolean;
}

export const CredentialForm = ({ onSubmit, isSubmitting }: CredentialFormProps) => {
  const { register, handleSubmit } = useForm<CredentialFormData>();

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div>
        <Label htmlFor="name">Credential Name</Label>
        <Input id="name" type="text" {...register('name', { required: true })} placeholder="e.g., My Work SendGrid" />
      </div>
      <div>
        <Label htmlFor="integrationType">Integration Type</Label>
        <Input id="integrationType" type="text" {...register('integrationType', { required: true })} placeholder="e.g., SendGrid" />
      </div>
      <div>
        <Label htmlFor="credentials">API Key / Secret</Label>
        <Input id="credentials" type="password" {...register('credentials', { required: true })} />
      </div>
      <div className="flex justify-end pt-4">
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving...' : 'Save Credential'}
        </Button>
      </div>
    </form>
  );
};
