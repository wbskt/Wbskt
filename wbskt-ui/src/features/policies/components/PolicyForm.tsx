import { useForm } from 'react-hook-form';
import type { PolicyRecord } from '../../../types/api';
import { Label } from '../../../components/common/Label';
import { Input } from '../../../components/common/Input';
import { Button } from '../../../components/common/Button';

export type PolicyFormData = {
  name: string;
  maxClients: number | null;
  expiry: string | null;
};

interface PolicyFormProps {
  initialData?: PolicyRecord;
  onSubmit: (data: PolicyFormData) => void;
  isSubmitting: boolean;
}

export const PolicyForm = ({ initialData, onSubmit, isSubmitting }: PolicyFormProps) => {
  const { register, handleSubmit } = useForm<PolicyFormData>({
    defaultValues: {
      name: initialData?.name ?? '',
      maxClients: initialData?.maxClients ?? null,
      expiry: initialData?.expiry ? new Date(initialData.expiry).toISOString().slice(0, 16) : null,
    },
  });

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div>
        <Label htmlFor="name">Policy Name</Label>
        <Input id="name" type="text" {...register('name', { required: true })} />
      </div>
      <div>
        <Label htmlFor="maxClients">Max Clients (optional)</Label>
        <Input id="maxClients" type="number" {...register('maxClients', { valueAsNumber: true })} />
      </div>
      <div>
        <Label htmlFor="expiry">Expiry Date (optional)</Label>
        <Input id="expiry" type="datetime-local" {...register('expiry')} />
      </div>
      <div className="flex justify-end pt-4">
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving...' : 'Save Policy'}
        </Button>
      </div>
    </form>
  );
};
