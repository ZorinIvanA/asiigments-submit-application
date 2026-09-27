/**
 * Корневой компонент приложения (точка входа бутстрапа, селектор app-root).
 *
 * Относится к layout/ по NFR-003: это оболочка приложения. Сам каркас
 * ничего не рендерит, кроме <router-outlet />: экраны добавляются задачами
 * фич (features/), шапка и страница 403 — задачами layout/.
 */
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app-root.html',
  styleUrl: './app-root.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppRoot {}
