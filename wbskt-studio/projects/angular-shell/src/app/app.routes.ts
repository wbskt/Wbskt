import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout/layout';

export const routes: Routes = [
    {
        path: '',
        component: LayoutComponent,
        children: [
            { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
            // Placeholder for dashboard route
            { path: 'dashboard', component: class { } }, 
            // Placeholder for workflows route
            { path: 'workflows', component: class { } },
            // Placeholder for executions route
            { path: 'executions', component: class { } },
        ]
    }
];