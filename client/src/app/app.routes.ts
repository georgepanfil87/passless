import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'preview',
    title: 'Passless — design preview',
    loadComponent: () => import('./preview/preview.component').then(m => m.PreviewComponent),
  },
  // Temporary: the preview is the only screen that exists until the real
  // sign-in route is wired to the API.
  { path: '', pathMatch: 'full', redirectTo: 'preview' },
];
